//
// ListView_Rendering.cs
//
// Author:
//   Aaron Bockover <abockover@novell.com>
//
// Copyright (C) 2007-2008 Novell, Inc.
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
using Gdk;

using Hyena.Gui;
using Hyena.Gui.Theming;
using GtkColorClass=Hyena.Gui.Theming.GtkColorClass;

namespace Hyena.Data.Gui
{
    public delegate int ListViewRowHeightHandler (Widget widget);

    public partial class ListView<T> : ListViewBase
    {
        private Cairo.Context cairo_context;
        private CellContext cell_context;
        private Pango.Layout pango_layout;

        private List<int> selected_rows = new List<int> ();

        private Theme theme;
        protected Theme Theme {
            get { return theme; }
        }

        private bool changing_style = false;

        protected override void OnStyleSet (Style old_style)
        {
            if (changing_style) {
                return;
            }

            changing_style = true;
            GtkUtilities.AdaptGtkRcStyle (this, typeof (TreeView));
            changing_style = false;

            base.OnStyleSet (old_style);
            RecomputeRowHeight = true;
            theme = Hyena.Gui.Theming.ThemeEngine.CreateTheme (this);

            // Save the drawable so we can reuse it
            Gdk.Drawable drawable = cell_context != null ? cell_context.Drawable : null;

            if (pango_layout != null) {
                pango_layout.Dispose ();
                pango_layout = null;
            }

            cell_context = new CellContext ();
            cell_context.Theme = theme;
            cell_context.Widget = this;
            cell_context.Drawable = drawable;
        }

        protected override bool OnExposeEvent (EventExpose evnt)
        {
            Rectangle damage = new Rectangle ();
            foreach (Rectangle rect in evnt.Region.GetRectangles ()) {
                damage = damage.Union (rect);
            }

            cairo_context = CairoHelper.Create (evnt.Window);
            if (pango_layout == null) {
                pango_layout = CairoExtensions.CreateLayout (this, cairo_context);
            }

            cell_context.Context = cairo_context;
            cell_context.Layout = pango_layout;

            Theme.DrawFrameBackground (cairo_context, Allocation, true);
            if (header_visible && column_controller != null) {
                PaintHeader (damage);
            }

            if (HasFocus)
                Theme.DrawFrameBorderFocused (cairo_context, Allocation);
            else
                Theme.DrawFrameBorder (cairo_context, Allocation);

            if (Model != null) {
                PaintRows (damage);
            }

            PaintDraggingColumn (damage);

            ((IDisposable)cairo_context.Target).Dispose ();
            ((IDisposable)cairo_context).Dispose ();

            return true;
        }

        private void PaintHeader (Rectangle clip)
        {
            Rectangle rect = header_rendering_alloc;
            rect.Height += Theme.BorderWidth;
            clip.Intersect (rect);
            cairo_context.Rectangle (clip.X, clip.Y, clip.Width, clip.Height);
            cairo_context.Clip ();

            Theme.DrawHeaderBackground (cairo_context, header_rendering_alloc);

            Rectangle cell_area = new Rectangle ();
            cell_area.Y = header_rendering_alloc.Y;
            cell_area.Height = header_rendering_alloc.Height;

            cell_context.Clip = clip;
            cell_context.Opaque = true;
            cell_context.TextAsForeground = true;

            bool have_drawn_separator = false;

            for (int ci = 0; ci < column_cache.Length; ci++) {
                if (pressed_column_is_dragging && pressed_column_index == ci) {
                    continue;
                }

                cell_area.X = column_cache[ci].X1 + Theme.TotalBorderWidth + header_rendering_alloc.X - HadjustmentValue;
                cell_area.Width = column_cache[ci].Width;
                PaintHeaderCell (cell_area, ci, false, ref have_drawn_separator);
            }

            if (pressed_column_is_dragging && pressed_column_index >= 0) {
                cell_area.X = pressed_column_x_drag + Allocation.X - HadjustmentValue;
                cell_area.Width = column_cache[pressed_column_index].Width;
                PaintHeaderCell (cell_area, pressed_column_index, true, ref have_drawn_separator);
            }

            cairo_context.ResetClip ();
        }

        private void PaintHeaderCell (Rectangle area, int ci, bool dragging, ref bool have_drawn_separator)
        {
            if (ci < 0 || column_cache.Length <= ci)
                return;

            if (ci == ActiveColumn && HasFocus && HeaderFocused) {
                Theme.DrawColumnHeaderFocus (cairo_context, area);
            }

            if (dragging) {
                Theme.DrawColumnHighlight (cairo_context, area,
                    CairoExtensions.ColorShade (Theme.Colors.GetWidgetColor (GtkColorClass.Dark, StateType.Normal), 0.9));

                Cairo.Color stroke_color = CairoExtensions.ColorShade (Theme.Colors.GetWidgetColor (
                    GtkColorClass.Base, StateType.Normal), 0.0);
                stroke_color.A = 0.3;

                cairo_context.Color = stroke_color;
                cairo_context.MoveTo (area.X + 0.5, area.Y + 1.0);
                cairo_context.LineTo (area.X + 0.5, area.Bottom);
                cairo_context.MoveTo (area.Right - 0.5, area.Y + 1.0);
                cairo_context.LineTo (area.Right - 0.5, area.Bottom);
                cairo_context.Stroke ();
            }

            ColumnCell cell = column_cache[ci].Column.HeaderCell;

            if (cell != null) {
                cairo_context.Save ();
                cairo_context.Translate (area.X, area.Y);
                cell_context.Area = area;
                cell.Render (cell_context, StateType.Normal, area.Width, area.Height);
                cairo_context.Restore ();
            }

            if (!dragging && ci < column_cache.Length - 1 && (have_drawn_separator ||
                column_cache[ci].MaxWidth != column_cache[ci].MinWidth)) {
                have_drawn_separator = true;
                Theme.DrawHeaderSeparator (cairo_context, area, area.Right);
            }
        }

        private void PaintRows (Rectangle clip)
        {
            // TODO factor this out?
            // Render the sort effect to the GdkWindow.
            if (sort_column_index != -1 && (!pressed_column_is_dragging || pressed_column_index != sort_column_index)) {
                CachedColumn col = column_cache[sort_column_index];
                Theme.DrawRowRule (cairo_context,
                    list_rendering_alloc.X + col.X1 - HadjustmentValue,
                    header_rendering_alloc.Bottom + Theme.BorderWidth,
                    col.Width, list_rendering_alloc.Height + Theme.InnerBorderWidth * 2);
            }

            clip.Intersect (list_rendering_alloc);
            cairo_context.Rectangle (clip.X, clip.Y, clip.Width, clip.Height);
            cairo_context.Clip ();

            cell_context.Clip = clip;
            cell_context.TextAsForeground = false;

            int vadjustment_value = VadjustmentValue;
            int first_row = vadjustment_value / RowHeight;
            int last_row = Math.Min (model.Count, first_row + RowsInView);
            int offset = list_rendering_alloc.Y - vadjustment_value % RowHeight;

            Rectangle selected_focus_alloc = Rectangle.Zero;
            Rectangle single_list_alloc = new Rectangle ();

            single_list_alloc.X = list_rendering_alloc.X - HadjustmentValue;
            single_list_alloc.Y = offset;
            single_list_alloc.Width = list_rendering_alloc.Width + HadjustmentValue;
            single_list_alloc.Height = RowHeight;

            int selection_height = 0;
            int selection_y = 0;
            selected_rows.Clear ();

            for (int ri = first_row; ri < last_row; ri++) {
                if (Selection != null && Selection.Contains (ri)) {
                    if (selection_height == 0) {
                        selection_y = single_list_alloc.Y;
                    }

                    selection_height += single_list_alloc.Height;
                    selected_rows.Add (ri);

                    if (Selection.FocusedIndex == ri) {
                        selected_focus_alloc = single_list_alloc;
                    }
                } else {
                    if (rules_hint && ri % 2 != 0) {
                        Theme.DrawRowRule (cairo_context, single_list_alloc.X, single_list_alloc.Y,
                            single_list_alloc.Width, single_list_alloc.Height);
                    }

                    PaintReorderLine (ri, single_list_alloc);

                    if (Selection != null && Selection.FocusedIndex == ri && !Selection.Contains (ri) && HasFocus) {
                        CairoCorners corners = CairoCorners.All;

                        if (Selection.Contains (ri - 1)) {
                            corners &= ~(CairoCorners.TopLeft | CairoCorners.TopRight);
                        }

                        if (Selection.Contains (ri + 1)) {
                            corners &= ~(CairoCorners.BottomLeft | CairoCorners.BottomRight);
                        }

                        if (HasFocus && !HeaderFocused) // Cursor out of selection.
                            Theme.DrawRowCursor (cairo_context, single_list_alloc.X, single_list_alloc.Y,
                                                 single_list_alloc.Width, single_list_alloc.Height,
                                                 CairoExtensions.ColorShade (Theme.Colors.GetWidgetColor (GtkColorClass.Background, StateType.Selected), 0.85));
                    }

                    if (selection_height > 0) {
                        Cairo.Color selection_color = Theme.Colors.GetWidgetColor (GtkColorClass.Background, StateType.Selected);
                        if (!HasFocus || HeaderFocused)
                            selection_color = CairoExtensions.ColorShade (selection_color, 1.1);

                        Theme.DrawRowSelection (cairo_context, list_rendering_alloc.X, selection_y, list_rendering_alloc.Width, selection_height,
                                                true, true, selection_color, CairoCorners.All);
                        selection_height = 0;
                    }

                    PaintRow (ri, single_list_alloc, StateType.Normal);
                }

                single_list_alloc.Y += single_list_alloc.Height;
            }

            // In case the user is dragging to the end of the list
            PaintReorderLine (last_row, single_list_alloc);

            if (selection_height > 0) {
                Theme.DrawRowSelection (cairo_context, list_rendering_alloc.X, selection_y,
                    list_rendering_alloc.Width, selection_height);
            }

            if (Selection != null && Selection.Count > 1 &&
                !selected_focus_alloc.Equals (Rectangle.Zero) &&
                HasFocus && !HeaderFocused) { // Cursor inside selection.
                Theme.DrawRowCursor (cairo_context, selected_focus_alloc.X, selected_focus_alloc.Y,
                    selected_focus_alloc.Width, selected_focus_alloc.Height,
                    Theme.Colors.GetWidgetColor (GtkColorClass.Text, StateType.Selected));
            }

            foreach (int ri in selected_rows) {
                single_list_alloc.Y = offset + ((ri - first_row) * single_list_alloc.Height);
                PaintRow (ri, single_list_alloc, StateType.Selected);
            }

            cairo_context.ResetClip ();
        }

        private void PaintReorderLine (int row_index, Rectangle single_list_alloc)
        {
            if (row_index == drag_reorder_row_index && IsReorderable) {
                cairo_context.Save ();
                cairo_context.LineWidth = 1.0;
                cairo_context.Antialias = Cairo.Antialias.None;
                cairo_context.MoveTo (single_list_alloc.Left, single_list_alloc.Top);
                cairo_context.LineTo (single_list_alloc.Right, single_list_alloc.Top);
                cairo_context.Color = Theme.Colors.GetWidgetColor (GtkColorClass.Text, StateType.Normal);
                cairo_context.Stroke ();
                cairo_context.Restore ();
            }
        }

        private void PaintRow (int row_index, Rectangle area, StateType state)
        {
            if (column_cache == null) {
                return;
            }

            object item = model[row_index];
            bool opaque = IsRowOpaque (item);
            bool bold = IsRowBold (item);

            Rectangle cell_area = new Rectangle ();
            cell_area.Height = RowHeight;
            cell_area.Y = area.Y;

            for (int ci = 0; ci < column_cache.Length; ci++) {
                if (pressed_column_is_dragging && pressed_column_index == ci) {
                    continue;
                }

                cell_area.Width = column_cache[ci].Width;
                cell_area.X = column_cache[ci].X1 + area.X;
                PaintCell (item, ci, row_index, cell_area, opaque, bold, state, false);
            }

            if (pressed_column_is_dragging && pressed_column_index >= 0) {
                cell_area.Width = column_cache[pressed_column_index].Width;
                cell_area.X = pressed_column_x_drag + list_rendering_alloc.X -
                    list_interaction_alloc.X - HadjustmentValue;
                PaintCell (item, pressed_column_index, row_index, cell_area, opaque, bold, state, true);
            }
        }

        private void PaintCell (object item, int column_index, int row_index, Rectangle area, bool opaque, bool bold,
            StateType state, bool dragging)
        {
            ColumnCell cell = column_cache[column_index].Column.GetCell (0);
            cell.BindListItem (item);
            ColumnCellDataProvider (cell, item);

            ITextCell text_cell = cell as ITextCell;
            if (text_cell != null) {
                text_cell.FontWeight = bold ? Pango.Weight.Bold : Pango.Weight.Normal;
            }

            if (dragging) {
                Cairo.Color fill_color = Theme.Colors.GetWidgetColor (GtkColorClass.Base, StateType.Normal);
                fill_color.A = 0.5;
                cairo_context.Color = fill_color;
                cairo_context.Rectangle (area.X, area.Y, area.Width, area.Height);
                cairo_context.Fill ();
            }

            cairo_context.Save ();
            cairo_context.Translate (area.X, area.Y);
            cell_context.Area = area;
            cell_context.Opaque = opaque;
            cell.Render (cell_context, dragging ? StateType.Normal : state, area.Width, area.Height);
            cairo_context.Restore ();

            AccessibleCellRedrawn (column_index, row_index);
        }

        private void PaintDraggingColumn (Rectangle clip)
        {
            if (!pressed_column_is_dragging || pressed_column_index < 0) {
                return;
            }

            CachedColumn column = column_cache[pressed_column_index];

            int x = pressed_column_x_drag + Allocation.X + 1 - HadjustmentValue;

            Cairo.Color fill_color = Theme.Colors.GetWidgetColor (GtkColorClass.Base, StateType.Normal);
            fill_color.A = 0.45;

            Cairo.Color stroke_color = CairoExtensions.ColorShade (Theme.Colors.GetWidgetColor (
                GtkColorClass.Base, StateType.Normal), 0.0);
            stroke_color.A = 0.3;

            cairo_context.Rectangle (x, header_rendering_alloc.Bottom + 1, column.Width - 2,
                list_rendering_alloc.Bottom - header_rendering_alloc.Bottom - 1);
            cairo_context.Color = fill_color;
            cairo_context.Fill ();

            cairo_context.MoveTo (x - 0.5, header_rendering_alloc.Bottom + 0.5);
            cairo_context.LineTo (x - 0.5, list_rendering_alloc.Bottom + 0.5);
            cairo_context.LineTo (x + column.Width - 1.5, list_rendering_alloc.Bottom + 0.5);
            cairo_context.LineTo (x + column.Width - 1.5, header_rendering_alloc.Bottom + 0.5);

            cairo_context.Color = stroke_color;
            cairo_context.LineWidth = 1.0;
            cairo_context.Stroke ();
        }

        private void InvalidateList ()
        {
            if (IsRealized) {
                QueueDrawArea (list_rendering_alloc.X, list_rendering_alloc.Y, list_rendering_alloc.Width, list_rendering_alloc.Height);
            }
        }

        private void InvalidateHeader ()
        {
            if (IsRealized) {
                QueueDrawArea (header_rendering_alloc.X, header_rendering_alloc.Y, header_rendering_alloc.Width, header_rendering_alloc.Height);
            }
        }

        protected virtual void ColumnCellDataProvider (ColumnCell cell, object boundItem)
        {
        }

        private bool rules_hint = false;
        public bool RulesHint {
            get { return rules_hint; }
            set {
                rules_hint = value;
                InvalidateList ();
            }
        }

        private ListViewRowHeightHandler row_height_handler;
        public virtual ListViewRowHeightHandler RowHeightProvider {
            get { return row_height_handler; }
            set {
                if (value != row_height_handler) {
                    row_height_handler = value;
                    RecomputeRowHeight = true;
                }
            }
        }

        private bool recompute_row_height = true;
        protected bool RecomputeRowHeight {
            get { return recompute_row_height; }
            set {
                recompute_row_height = value;
                if (value && IsMapped && IsRealized) {
                    QueueDraw ();
                }
            }
        }

        private int row_height = 32;
        public int RowHeight {
            get {
                if (RecomputeRowHeight) {
                    row_height = RowHeightProvider != null
                        ? RowHeightProvider (this)
                        : ColumnCellText.ComputeRowHeight (this);

                    header_height = 0;
                    MoveResize (Allocation);

                    RecomputeRowHeight = false;
                }

                return row_height;
            }
        }
    }
}
