//
// SourceView.cs
//
// Author:
//   Aaron Bockover <abockover@novell.com>
//
// Copyright (C) 2005-2008 Novell, Inc.
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;

using Gtk;
using Cairo;
using Mono.Unix;

using Hyena.Gui.Theming;
using Hyena.Gui.Theatrics;

using Banshee.Configuration;
using Banshee.ServiceStack;
using Banshee.Sources;
using Banshee.Playlist;

using Banshee.Gui;

namespace Banshee.Sources.Gui
{
    // Note: This is a partial class - the drag and drop code is split
    //       out into a separate file to make this class more manageable.
    //       See SourceView_DragAndDrop.cs for the DnD code.

    public partial class SourceView : TreeView
    {
        private SourceRowRenderer renderer;
        private Theme theme;
        private Cairo.Context cr;

        private Stage<TreeIter> notify_stage = new Stage<TreeIter> (2000);

        private TreeViewColumn focus_column;
        private TreePath highlight_path;
        private SourceModel store;
        private int current_timeout = -1;
        private bool editing_row = false;
        private bool need_resort = false;

        public SourceView ()
        {
            BuildColumns ();

            store = new SourceModel ();
            store.SourceRowInserted += OnSourceRowInserted;
            store.SourceRowRemoved += OnSourceRowRemoved;
            store.RowChanged += OnRowChanged;
            Model = store;

            ConfigureDragAndDrop ();
            store.Refresh ();
            ConnectEvents ();

            RowSeparatorFunc = RowSeparatorHandler;
        }

#region Setup Methods

        private void BuildColumns ()
        {
            // Hidden expander column
            TreeViewColumn col = new TreeViewColumn ();
            col.Visible = false;
            AppendColumn (col);
            ExpanderColumn = col;

            focus_column = new TreeViewColumn ();
            renderer = new SourceRowRenderer ();
            renderer.RowHeight = RowHeight.Get ();
            renderer.Padding = RowPadding.Get ();
            focus_column.PackStart (renderer, true);
            focus_column.SetCellDataFunc (renderer, new CellLayoutDataFunc (SourceRowRenderer.CellDataHandler));
            AppendColumn (focus_column);

            HeadersVisible = false;
        }

        private void ConnectEvents ()
        {
            ServiceManager.SourceManager.ActiveSourceChanged += delegate (SourceEventArgs args) {
                Banshee.Base.ThreadAssist.ProxyToMain (ResetSelection);
            };

            ServiceManager.SourceManager.SourceUpdated += delegate (SourceEventArgs args) {
                Banshee.Base.ThreadAssist.ProxyToMain (delegate {
                    lock (args.Source) {
                        TreeIter iter = store.FindSource (args.Source);
                        if (!TreeIter.Zero.Equals (iter)) {
                            need_resort = true;
                            QueueDraw ();
                        }
                    }
                });
            };

            ServiceManager.PlaybackController.NextSourceChanged += delegate {
                Banshee.Base.ThreadAssist.ProxyToMain (QueueDraw);
            };

            notify_stage.ActorStep += delegate (Actor<TreeIter> actor) {
                Banshee.Base.ThreadAssist.AssertInMainThread ();
                if (!store.IterIsValid (actor.Target)) {
                    return false;
                }

                Gdk.Rectangle rect = GetBackgroundArea (store.GetPath (actor.Target), focus_column);
                QueueDrawArea (rect.X, rect.Y, rect.Width, rect.Height);
                return true;
            };
        }

#endregion

#region Gtk.Widget Overrides

        protected override void OnRealized ()
        {
            base.OnRealized ();

            theme = new GtkTheme (this);
            // theme.RefreshColors ();
        }

        protected override bool OnButtonPressEvent (Gdk.EventButton press)
        {
            TreePath path;
            TreeViewColumn column;

            if (press.Button == 1) {
                ResetHighlight ();
            }

            // If there is not a row at the click position let the base handler take care of the press
            if (!GetPathAtPos ((int)press.X, (int)press.Y, out path, out column)) {
                return base.OnButtonPressEvent (press);
            }

            Source source = store.GetSource (path);

            // From F-Spot's SaneTreeView class
            int expander_size = (int)StyleGetProperty ("expander-size");
            int horizontal_separator = (int)StyleGetProperty ("horizontal-separator");
            bool on_expander = press.X <= horizontal_separator * 2 + path.Depth * expander_size;

            if (on_expander) {
                bool ret = base.OnButtonPressEvent (press);
                // If the active source is a child of this source, and we are about to collapse it, switch
                // the active source to the parent.
                if (source == ServiceManager.SourceManager.ActiveSource.Parent && GetRowExpanded (path)) {
                    ServiceManager.SourceManager.SetActiveSource (source);
                }
                return ret;
            }

            // For Sources that can't be activated, when they're clicked just
            // expand or collapse them and return.
            if (press.Button == 1 && !source.CanActivate) {
                if (!source.Expanded) {
                    ExpandRow (path, false);
                } else {
                    CollapseRow (path);
                }
                return false;
            }

            if (press.Button == 3) {
                HighlightPath (path);
                OnPopupMenu ();
                return true;
            }

            if (!source.CanActivate) {
                return false;
            }


            if (press.Button == 1) {
                if (ServiceManager.SourceManager.ActiveSource != source) {
                    ServiceManager.SourceManager.SetActiveSource (source);
                }
            }

            if ((press.State & Gdk.ModifierType.ControlMask) != 0) {
                if (press.Type == Gdk.EventType.TwoButtonPress && press.Button == 1) {
                    ActivateRow (path, null);
                }
                return true;
            }

            return base.OnButtonPressEvent (press);
        }

        protected override bool OnPopupMenu ()
        {
            ServiceManager.Get<InterfaceActionService> ().SourceActions["SourceContextMenuAction"].Activate ();
            return true;
        }

        protected override bool OnExposeEvent (Gdk.EventExpose evnt)
        {
            if (need_resort) {
                need_resort = false;

                // Resort the tree store. This is performed in an event handler
                // known not to conflict with gtk_tree_view_bin_expose() to prevent
                // errors about corrupting the TreeView's internal state.
                foreach (Source dsource in ServiceManager.SourceManager.Sources) {
                    TreeIter iter = store.FindSource (dsource);
                    if (!TreeIter.Zero.Equals (iter) && (int)store.GetValue (iter, 1) != dsource.Order) {
                        store.SetValue (iter, 1, dsource.Order);
                    }
                }
                QueueDraw ();
            }

            try {
                cr = Gdk.CairoHelper.Create (evnt.Window);
                return base.OnExposeEvent (evnt);
            } finally {
                ((IDisposable)cr.Target).Dispose ();
                ((IDisposable)cr).Dispose ();
                cr = null;
            }
        }

#endregion

#region Gtk.TreeView Overrides

        protected override void OnRowExpanded (TreeIter iter, TreePath path)
        {
            base.OnRowExpanded (iter, path);
            store.GetSource (iter).Expanded = true;
        }

        protected override void OnRowCollapsed (TreeIter iter, TreePath path)
        {
            base.OnRowCollapsed (iter, path);
            store.GetSource (iter).Expanded = false;
        }

        protected override void OnCursorChanged ()
        {
            if (current_timeout < 0) {
                current_timeout = (int)GLib.Timeout.Add (200, OnCursorChangedTimeout);
            }
        }

        private bool OnCursorChangedTimeout ()
        {
            TreeIter iter;
            TreeModel model;

            current_timeout = -1;

            if (!Selection.GetSelected (out model, out iter)) {
                return false;
            }

            Source new_source = store.GetValue (iter, 0) as Source;
            if (ServiceManager.SourceManager.ActiveSource == new_source) {
                return false;
            }

            ServiceManager.SourceManager.SetActiveSource (new_source);

            QueueDraw ();

            return false;
        }

        private bool RowSeparatorHandler (TreeModel model, TreeIter iter)
        {
            return (bool)store.GetValue (iter, 2);
        }

#endregion

#region Add/Remove Sources / SourceManager interaction

        private void OnSourceRowInserted (object o, SourceRowEventArgs args)
        {
            args.Source.UserNotifyUpdated += OnSourceUserNotifyUpdated;

            if (args.Source.Parent != null && args.Source.Parent.AutoExpand == true) {
                Expand (args.ParentIter);
            }

            if (args.Source.Expanded || args.Source.AutoExpand == true) {
                Expand (args.Iter);
            }

            UpdateView ();
        }

        private void OnSourceRowRemoved (object o, SourceRowEventArgs args)
        {
            args.Source.UserNotifyUpdated -= OnSourceUserNotifyUpdated;
            UpdateView ();
        }

        private void OnRowChanged (object o, RowChangedArgs args)
        {
            QueueDraw ();
        }

        private void Expand (TreeIter iter)
        {
            TreePath path = store.GetPath (iter);
            ExpandRow (path, true);
        }

        private void OnSourceUserNotifyUpdated (object o, EventArgs args)
        {
            Banshee.Base.ThreadAssist.ProxyToMain (delegate {
                TreeIter iter = store.FindSource ((Source)o);
                if (iter.Equals (TreeIter.Zero)) {
                    return;
                }

                notify_stage.AddOrReset (iter);
            });
        }

#endregion

#region List/View Utility Methods

        private bool UpdateView ()
        {
            for (int i = 0, m = store.IterNChildren (); i < m; i++) {
                TreeIter iter = TreeIter.Zero;
                if (!store.IterNthChild (out iter, i)) {
                    continue;
                }

                if (store.IterNChildren (iter) > 0) {
                    ExpanderColumn = Columns[1];
                    return true;
                }
            }

            ExpanderColumn = Columns[0];
            return false;
        }

        internal void UpdateRow (TreePath path, string text)
        {
            TreeIter iter;

            if (!store.GetIter (out iter, path)) {
                return;
            }

            Source source = store.GetValue (iter, 0) as Source;
            source.Rename (text);
        }

        public void BeginRenameSource (Source source)
        {
            TreeIter iter = store.FindSource (source);
            if (iter.Equals (TreeIter.Zero)) {
                return;
            }

            renderer.Editable = true;
            SetCursor (store.GetPath (iter), focus_column, true);
            renderer.Editable = false;
        }

        private void ResetSelection ()
        {
            TreeIter iter = store.FindSource (ServiceManager.SourceManager.ActiveSource);

            if (!iter.Equals (TreeIter.Zero)){
                Selection.SelectIter (iter);
            }
        }

        public void HighlightPath (TreePath path)
        {
            highlight_path = path;
            QueueDraw ();
        }

        public void ResetHighlight ()
        {
            highlight_path = null;
            QueueDraw ();
        }

#endregion

#region Public Properties

        public Source HighlightedSource {
            get {
                TreeIter iter;

                if (highlight_path == null || !store.GetIter (out iter, highlight_path)) {
                    return null;
                }

                return store.GetValue (iter, 0) as Source;
            }
        }

        public bool EditingRow {
            get { return editing_row; }
            set {
                editing_row = value;
                QueueDraw ();
            }
        }

#endregion

#region Internal Properties

        internal TreePath HighlightedPath {
            get { return highlight_path; }
        }

        internal Cairo.Context Cr {
            get { return cr; }
        }

        internal Theme Theme {
            get { return theme; }
        }

        internal Stage<TreeIter> NotifyStage {
            get { return notify_stage; }
        }

        internal Source NewPlaylistSource {
            get {
                return new_playlist_source ??
                    (new_playlist_source = new PlaylistSource (Catalog.GetString ("New Playlist"),
                        ServiceManager.SourceManager.MusicLibrary));
            }
        }

#endregion

#region Property Schemas

        private static SchemaEntry<int> RowHeight = new SchemaEntry<int> (
            "player_window", "source_view_row_height", 22, "The height of each source row in the SourceView.  22 is the default.", "");

        private static SchemaEntry<int> RowPadding = new SchemaEntry<int> (
            "player_window", "source_view_row_padding", 5, "The padding between sources in the SourceView.  5 is the default.", "");

#endregion
    }
}
