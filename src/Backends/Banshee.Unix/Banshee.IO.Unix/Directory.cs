//
// Directory.cs
//
// Author:
//   Aaron Bockover <abockover@novell.com>
//
// Copyright (C) 2006-2008 Novell, Inc.
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
using System.IO;
using System.Collections.Generic;
using Mono.Unix;

using Hyena;
using Banshee.Base;

namespace Banshee.IO.Unix
{
    public class Directory : IDirectory
    {
        [System.Runtime.InteropServices.DllImport ("libglib-2.0-0.dll")]
        private static extern int g_mkdir_with_parents (IntPtr path, int mode);

        public void Create (string directory)
        {
            IntPtr path_ptr = IntPtr.Zero;

            try {
                path_ptr = GLib.Marshaller.StringToPtrGStrdup (directory);
                if (path_ptr == IntPtr.Zero) {
                    throw new Exception ("Failed to allocate native directory string");
                }

                // 493 == 0755 - C# doesn't do octal literals
                if (g_mkdir_with_parents (path_ptr, 493) == -1) {
                    Mono.Unix.UnixMarshal.ThrowExceptionForLastError ();
                }
            } catch (EntryPointNotFoundException) {
                Log.Warning ("g_mkdir_with_parents could not be found, falling back to System.IO");
                System.IO.Directory.CreateDirectory (directory);
            } finally {
                if (!path_ptr.Equals (IntPtr.Zero)) {
                    GLib.Marshaller.Free (path_ptr);
                }
            }
        }

        public void Delete (string directory)
        {
            Delete (directory, false);
        }

        public void Delete (string directory, bool recursive)
        {
            UnixDirectoryInfo unix_dir = new UnixDirectoryInfo (directory);
            unix_dir.Delete (recursive);
        }

        public bool Exists (string directory)
        {
            try {
                FileStat stat = new FileStat (directory);
                return stat.IsDirectory;
            } catch {
                return false;
            }
        }

        public IEnumerable<string> GetFiles (string directory)
        {
            UnixDirectoryInfo unix_dir = new UnixDirectoryInfo (directory);
            foreach (UnixFileSystemInfo entry in unix_dir.GetFileSystemEntries ()) {
                if (!entry.IsDirectory && entry.IsRegularFile && !entry.IsSocket && entry.Exists) {
                    yield return entry.FullName;
                }
            }
        }

        public IEnumerable<string> GetDirectories (string directory)
        {
            UnixDirectoryInfo unix_dir = new UnixDirectoryInfo (directory);
            foreach (UnixFileSystemInfo entry in unix_dir.GetFileSystemEntries ()) {
                if (entry.IsDirectory && entry.Exists && !entry.IsSocket) {
                    yield return entry.FullName;
                }
            }
        }

        public void Move (SafeUri from, SafeUri to)
        {
            Mono.Unix.Native.Stdlib.rename (from.LocalPath, to.LocalPath);
        }
    }
}
