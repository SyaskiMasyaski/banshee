//
// DatabaseAlbumInfo.cs
//
// Author:
//   Aaron Bockover <abockover@novell.com>
//
// Copyright (C) 2007 Novell, Inc.
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
using System.Data;

using Mono.Unix;

using Hyena.Data.Sqlite;

using Banshee.Database;
using Banshee.ServiceStack;

namespace Banshee.Collection.Database
{
    public class DatabaseAlbumInfo : AlbumInfo, ICacheableItem
    {
        private static BansheeModelProvider<DatabaseAlbumInfo> provider = new BansheeModelProvider<DatabaseAlbumInfo> (
            ServiceManager.DbConnection, "CoreAlbums"
        );

        public static BansheeModelProvider<DatabaseAlbumInfo> Provider {
            get { return provider; }
        }

        private static HyenaSqliteCommand select_command = new HyenaSqliteCommand (String.Format (
            "SELECT {0} FROM {1} WHERE {2} AND CoreAlbums.ArtistID = ? AND CoreAlbums.Title = ?",
            provider.Select, provider.From,
            (String.IsNullOrEmpty (provider.Where) ? "1=1" : provider.Where)
        ));

        private enum Column : int {
            AlbumID,
            Title,
            ArtistName
        }

        private static int last_artist_id;
        private static string last_title;
        private static DatabaseAlbumInfo last_album;

        public static DatabaseAlbumInfo FindOrCreate (DatabaseArtistInfo artist, string title)
        {
            DatabaseAlbumInfo album = new DatabaseAlbumInfo ();
            album.Title = title;
            return FindOrCreate (artist, album);
        }

        public static DatabaseAlbumInfo FindOrCreate (DatabaseArtistInfo artist, DatabaseAlbumInfo album)
        {
            if (album.Title == last_title && artist.DbId == last_artist_id && last_album != null) {
                return last_album;
            }

            if (String.IsNullOrEmpty (album.Title) || album.Title.Trim () == String.Empty) {
                album.Title = Catalog.GetString ("Unknown Album");
            }

            using (IDataReader reader = ServiceManager.DbConnection.Query (select_command, artist.DbId, album.Title)) {
                if (reader.Read ()) {
                    last_album = provider.Load (reader);
                    last_album.ArtistId = artist.DbId;
                    last_album.ArtistName = artist.Name;
                } else {
                    last_album = album;
                    last_album.ArtistId = artist.DbId;
                    last_album.ArtistName = artist.Name;
                    last_album.Save ();
                }
            }
            
            last_title = album.Title;
            last_artist_id = artist.DbId;
            return last_album;
        }

        public static DatabaseAlbumInfo UpdateOrCreate (DatabaseArtistInfo artist, DatabaseAlbumInfo album)
        {
            DatabaseAlbumInfo found = FindOrCreate (artist, album);
            if (found != album) {
                // Overwrite the found album
                album.Title = found.Title;
                album.dbid = found.DbId;
                album.ArtistId = found.ArtistId;
                album.Save ();
            }
            return album;
        }

        public DatabaseAlbumInfo () : base (null)
        {
        }

        public void Save ()
        {
            Provider.Save (this);
        }

        [DatabaseColumn("AlbumID", Constraints = DatabaseColumnConstraints.PrimaryKey)]
        private int dbid;
        public int DbId {
            get { return dbid; }
        }

        private long cache_entry_id;
        public long CacheEntryId {
            get { return cache_entry_id; }
            set { cache_entry_id = value; }
        }

        private long cache_model_id;
        public long CacheModelId {
            get { return cache_model_id; }
            set { cache_model_id = value; }
        }

        [DatabaseColumn("ArtistID")]
        private int artist_id;
        public int ArtistId {
            get { return artist_id; }
            set { artist_id = value; }
        }
        
        [DatabaseColumn("MusicBrainzID")]
        public override string MusicBrainzId {
            get { return base.MusicBrainzId; }
            set { base.MusicBrainzId = value; }
        }
        
        [DatabaseColumn]
        public override DateTime ReleaseDate {
            get { return base.ReleaseDate; }
            set { base.ReleaseDate = value; }
        }

        [DatabaseColumn]
        public override string Title {
            get { return base.Title; }
            set { base.Title = value; }
        }
        
        [DatabaseColumn(Select = false)]
        protected string TitleLowered {
            get { return Title == null ? null : Title.ToLower (); }
        }

        [VirtualDatabaseColumn("Name", "CoreArtists", "ArtistID", "ArtistID")]
        public override string ArtistName {
            get { return base.ArtistName; }
            set { base.ArtistName = value; }
        }

        public override string ToString ()
        {
            return String.Format ("<LibraryAlbumInfo Title={0} DbId={1}>", Title, DbId);
        }
    }
}
