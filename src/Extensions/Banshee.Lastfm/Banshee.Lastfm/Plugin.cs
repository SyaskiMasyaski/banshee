/***************************************************************************
 *  Plugin.cs
 *
 *  Copyright (C) 2007-2008 Novell, Inc.
 *  Written by Gabriel Burt <gabriel.burt@gmail.com>
 ****************************************************************************/

/*  THIS FILE IS LICENSED UNDER THE MIT LICENSE AS OUTLINED IMMEDIATELY BELOW: 
 *
 *  Permission is hereby granted, free of charge, to any person obtaining a
 *  copy of this software and associated documentation files (the "Software"),  
 *  to deal in the Software without restriction, including without limitation  
 *  the rights to use, copy, modify, merge, publish, distribute, sublicense,  
 *  and/or sell copies of the Software, and to permit persons to whom the  
 *  Software is furnished to do so, subject to the following conditions:
 *
 *  The above copyright notice and this permission notice shall be included in 
 *  all copies or substantial portions of the Software.
 *
 *  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR 
 *  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
 *  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
 *  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
 *  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
 *  FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
 *  DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Data;
using System.Collections.Generic;
using Gtk;

using Mono.Unix;

using Banshee.Base;
using Banshee.Sources;
using Banshee.Widgets;
using Banshee.MediaEngine;
using Banshee.Database;
using Banshee.Configuration;

public static class PluginModuleEntry
{
    public static Type [] GetTypes ()
    {
        return new Type [] {
            typeof (Banshee.Plugins.LastFM.LastFMPlugin)
        };
    }
}

namespace Banshee.Lastfm
{
    public class LastFMPlugin : Banshee.Plugins.Plugin
    {
        private ActionButton love_button;
        private ActionButton hate_button;
        private bool last_track_was_lastfm = false;

        private LastFMSource source;
        public Source Source {
            get { return source; }
        }

        private static LastFMPlugin instance = null;
        public static LastFMPlugin Instance {
            get { return instance; }
        }

        protected override string ConfigurationName { 
            get { return "lastfm"; } 
        }
        
        public override string DisplayName { 
            get { return Catalog.GetString ("Last.fm Radio"); }
        }
        
        public override string Description {
            get { return Catalog.GetString ("Play music from Last.fm, the world's largest social music platform. Show off your taste, see what your friends are listening to, hear new music, get personal radio, recommendations, and downloads, all for free."); }
        }
        
        public override string [] Authors {
            get { return new string [] { "Gabriel Burt" }; }
        }
 
        protected override void PluginInitialize ()
        {
            instance = this;

            // We don't automatically connect to Last.fm, but load the last Last.fm
            // username we used so we can load the user's stations.
            string last_user = LastUserSchema.Get ();
            if (last_user != null && last_user != String.Empty)
                Last.FM.Account.Username = last_user;
        }

        private uint actions_id;
        private ActionGroup actions;
        protected override void InterfaceInitialize ()
        {
            source = new LastFMSource ();

            actions = new ActionGroup ("LastFM");
            actions.Add (new ActionEntry [] {
                new ActionEntry (
                    "LastFMAddAction", Stock.Add,
                     Catalog.GetString ("_Add Station"),
                     null, "", OnAddStation
                ),
                new ActionEntry (
                    "LastFMConnectAction", null,
                     Catalog.GetString ("Connect"),
                     null, "", OnConnect
                ),
                new ActionEntry (
                    "LastFMSortAction", "gtk-sort-descending",
                    Catalog.GetString ("Sort Stations by"),
                    null, "", null
                ),
                new ActionEntry (
                    "LastFMLoveAction", "face-smile",
                    Catalog.GetString ("Love Track"), null,
                    Catalog.GetString ("Mark current track as loved"), OnLoved
                ),
                new ActionEntry (
                    "LastFMHateAction", "face-sad",
                    Catalog.GetString ("Ban Track"), null,
                    Catalog.GetString ("Mark current track as banned"), OnHated
                )
            });

            actions.Add (
                new RadioActionEntry [] {
                    new RadioActionEntry (
                        "LastFMSortStationsByNameAction", null,
                         Catalog.GetString ("Station Name"),
                         null, "", 0
                    ),
                    new RadioActionEntry (
                        "LastFMSortStationsByPlayCountAction", null,
                         Catalog.GetString ("Total Play Count"),
                         null, "", 1
                    ),
                    new RadioActionEntry (
                        "LastFMSortStationsByTypeAction", null,
                         Catalog.GetString ("Station Type"),
                         null, "", 2
                    )
                },
                Array.IndexOf (LastFMSource.ChildComparers, source.ChildComparer),
                delegate (object sender, ChangedArgs args) {
                    source.ChildComparer = LastFMSource.ChildComparers[args.Current.Value];
                    source.SortChildSources ();
                }
            );

            Globals.ActionManager.UI.InsertActionGroup (actions, 0);
            actions_id = Globals.ActionManager.UI.AddUiFromResource ("Actions.xml");

            Globals.ActionManager["LastFMLoveAction"].IsImportant = true;
            Globals.ActionManager["LastFMHateAction"].IsImportant = true;
            Globals.ActionManager["LastFMLoveAction"].Visible = false;
            Globals.ActionManager["LastFMHateAction"].Visible = false;
            ActionButton love_button = new ActionButton (actions.GetAction ("LastFMLoveAction"));
            love_button.Pixbuf = IconThemeUtils.LoadIcon ("face-smile", 22);
            love_button.Padding = 1;
            ActionButton hate_button = new ActionButton (actions.GetAction ("LastFMHateAction"));
            hate_button.Pixbuf = IconThemeUtils.LoadIcon ("face-sad", 22);
            hate_button.Padding = 1;
            InterfaceElements.ActionButtonBox.PackStart (love_button, false, false, 0);
            InterfaceElements.ActionButtonBox.PackStart (hate_button, false, false, 0);

            PlayerEngineCore.EventChanged += delegate (object o, PlayerEngineEventArgs args) {
                if (args.Event == PlayerEngineEvent.TrackInfoUpdated) {
                    if (PlayerEngineCore.CurrentTrack is LastFMTrackInfo) {
                        if (!last_track_was_lastfm) {
                            Globals.ActionManager["LastFMLoveAction"].Visible = true;
                            Globals.ActionManager["LastFMHateAction"].Visible = true;
                            last_track_was_lastfm = true;
                        }
                    } else {
                        if (last_track_was_lastfm) {
                            Globals.ActionManager["LastFMLoveAction"].Visible = false;
                            Globals.ActionManager["LastFMHateAction"].Visible = false;
                            last_track_was_lastfm = false;
                        }
                    }
                }
            };

            SourceManager.AddSource (source);
            source.Initialize ();
        }

        protected override void PluginDispose ()
        {
            if (source != null) {
                source.Dispose ();
                SourceManager.RemoveSource (source);
            }

            //InterfaceElements.ActionButtonBox.Remove (love_button);
            //InterfaceElements.ActionButtonBox.Remove (hate_button);

            Connection.Instance.Dispose ();
            Globals.ActionManager.UI.RemoveUi (actions_id);
            Globals.ActionManager.UI.RemoveActionGroup (actions);
            actions = null;
            instance = null;
        }

        private void OnAddStation (object sender, EventArgs args)
        {
            StationEditor ed = new StationEditor ();
            ed.Window.ShowAll ();
            ed.RunDialog ();
        }

        private void OnConnect (object sender, EventArgs args)
        {
            Connection.Instance.Connect ();
        }

        private void OnChangeStation (object sender, EventArgs args)
        {
            (SourceManager.ActiveSource as StationSource).ChangeToThisStation ();
        }

        private void OnRefreshStation (object sender, EventArgs args)
        {
            (SourceManager.ActiveSource as StationSource).Refresh ();
        }

        private void OnLoved (object sender, EventArgs args)
        {
            LastFMTrackInfo track = PlayerEngineCore.CurrentTrack as LastFMTrackInfo;
            if (track == null)
                return;

            track.Love ();
        }

        private void OnHated (object sender, EventArgs args)
        {
            LastFMTrackInfo track = PlayerEngineCore.CurrentTrack as LastFMTrackInfo;
            if (track == null)
                return;

            track.Ban ();
            Globals.ActionManager["NextAction"].Activate ();
        }

        public static readonly SchemaEntry<bool> EnabledSchema = new SchemaEntry<bool> (
            "plugins.lastfm", "enabled", false, "Plugin enabled", "Last.fm plugin enabled"
        );

        public static readonly SchemaEntry<int> StationSortSchema = new SchemaEntry<int> (
            "plugins.lastfm", "station_sort", 0, "Station sort criteria", "Last.fm station sort criteria. 0 = name, 1 = play count, 2 = type"
        );

        public static readonly SchemaEntry<string> LastUserSchema = new SchemaEntry<string> (
            "plugins.lastfm", "username", "", "Last.fm user", "Last.fm username"
        );

        public static readonly SchemaEntry<bool> ExpandedSchema = new SchemaEntry<bool> (
            "plugins.lastfm", "expanded", false, "Last.fm expanded", "Last.fm expanded"
        );
    }
}