//
// TitledList.cs
//
// Authors:
//   Gabriel Burt <gburt@novell.com>
//
// Copyright (C) 2008 Novell, Inc.
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

using Gtk;

namespace Banshee.Gui.Widgets
{
    public class TitledList : VBox
    {
        private Label title;

        public string Title {
            get { return title.Text; }
            set {
                title.Markup = String.Format ("<b>{0}</b>", GLib.Markup.EscapeText (value));
            }
        }

        public int TitleWidthChars {
            get { return title.WidthChars; }
            set { title.WidthChars = value; }
        }

        public TitledList (string title_str) : base (false, 3)
        {
            title = new Label ();
            title.Xalign = 0;
            title.Ellipsize = Pango.EllipsizeMode.End;
            Title = title_str;

            PackStart (title, false, false, 0);
            title.Show ();

            StyleSet += delegate {
                title.ModifyBg (StateType.Normal, Style.Base (StateType.Normal));
                title.ModifyFg (StateType.Normal, Style.Text (StateType.Normal));
            };
        }
    }
}
