/***************************************************************************
 *  PodcastFeedView.cs
 *
 *  Copyright (C) 2007 Michael C. Urbanski
 *  Written by Mike Urbanski <michael.c.urbanski@gmail.com>
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
using System.Collections.ObjectModel;

using Mono.Unix;

using Gtk;

using Hyena.Data;
using Hyena.Data.Gui;
using Hyena.Collections;

using Banshee.Gui;
using Banshee.ServiceStack;
using Banshee.Collection.Gui;

using Banshee.Podcasting.Data;

using Migo.Syndication;

namespace Banshee.Podcasting.Gui
{
    public class PodcastFeedView : TrackFilterListView<Feed>
    {
        public PodcastFeedView () : base ()
        {
            ColumnCellPodcast renderer = new ColumnCellPodcast ();
            column_controller.Add (new Column ("Podcast", renderer, 1.0));
            //column_controller.Add (new Column (null, "Activity", new FeedActivityColumnCell ("Activity"), 0.00, true, 26, 26));

            ColumnController = column_controller;
            RowHeightProvider = renderer.ComputeRowHeight;
        }

        protected override bool OnPopupMenu ()
        {
            ServiceManager.Get<InterfaceActionService> ().FindAction ("Podcast.PodcastFeedPopupAction").Activate ();
            return true;
        }
    }

    /*public class PodcastFeedView : ListView<Feed>
    {
        private ColumnController columnController;

        public PodcastFeedView () : base ()
        {
            columnController = new ColumnController ();

            SortableColumn podcastFeedTitleColumn = new SortableColumn (
                    Catalog.GetString ("Podcasts"),
                    new ColumnCellText ("Title", true), 0.97,
                    PodcastSortKeys.Title, true
            );

            columnController.AddRange (
                new Column (null, "Activity", new FeedActivityColumnCell ("Activity"), 0.00, true, 26, 26),
                podcastFeedTitleColumn
            );

            RulesHint = true;
            podcastFeedTitleColumn.SortType = Hyena.Data.SortType.Descending;
            columnController.DefaultSortColumn = podcastFeedTitleColumn;
            ColumnController = columnController;
        }

        private Menu allPopup;
        private Menu popupMenu;
        private MenuItem homepageItem;
        private MenuItem propertiesItem;
        private MenuItem updateAllItem;

        protected override bool OnPopupMenu ()
        {
            if (popupMenu == null) {
                UIManager uiManager = ServiceManager.Get<InterfaceActionService> ().UIManager;

                allPopup = uiManager.GetWidget ("/PodcastSourcePopup") as Menu;
                popupMenu = uiManager.GetWidget ("/PodcastFeedViewPopup") as Menu;

                updateAllItem = uiManager.GetWidget ("/PodcastFeedViewPopup/PodcastUpdateAll") as MenuItem;
                propertiesItem = uiManager.GetWidget ("/PodcastFeedViewPopup/PodcastProperties") as MenuItem;
                homepageItem = uiManager.GetWidget ("/PodcastFeedViewPopup/PodcastHomepage") as MenuItem;
            }

            Menu popup;
            PodcastFeedModel model = Model as PodcastFeedModel;

            ReadOnlyCollection<Feed> feeds = model.CopySelectedItems ();

            if (feeds.Count == 0) {
                popup = allPopup;
            } else {
                if (feeds.Count > 1) {
                    homepageItem.Hide ();
                    propertiesItem.Hide ();
                } else {
                    homepageItem.Show ();
                    propertiesItem.Show ();
                }

                popup = popupMenu;
            }

            updateAllItem.Hide ();

            popup.Popup (null, null, null, 0, Gtk.Global.CurrentEventTime);
            return true;
        }
    }*/
}
