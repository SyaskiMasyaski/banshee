//
// DaapService.cs
//
// Authors:
//   Alexander Hixon <hixon.alexander@mediati.org>
//
// Copyright (C) 2008 Alexander Hixon
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
using Mono.Unix;
using Daap;

using Banshee.Base;
using Banshee.Collection;
using Banshee.Sources;
using Banshee.ServiceStack;

namespace Banshee.Daap
{
    public class DaapService : IExtensionService, IDisposable, IDelayedInitializeService
    {
        private ServiceLocator locator;
        private DateTime locator_started;
        private static DaapProxyWebServer proxy_server;

        private DaapContainerSource container;
        private Dictionary<string, DaapSource> source_map;

        internal static DaapProxyWebServer ProxyServer {
            get { return proxy_server; }
        }

        void IExtensionService.Initialize ()
        {
        }

        public void Dispose ()
        {
            if (locator != null) {
                locator.Stop ();
                locator.Found -= OnServiceFound;
                locator.Removed -= OnServiceRemoved;
                locator = null;
            }

            if (proxy_server != null) {
                proxy_server.Stop ();
                proxy_server = null;
            }

            // Dispose any remaining child sources
            if (source_map != null) {
                foreach (KeyValuePair <string, DaapSource> kv in source_map) {
                    if (kv.Value != null) {
                        kv.Value.Disconnect (true);
                        kv.Value.Dispose ();
                    }
                }

                source_map.Clear ();
            }

            if (container != null) {
                ServiceManager.SourceManager.RemoveSource (container, true);
                container = null;
            }
        }

        private void OnServiceFound (object o, ServiceArgs args)
        {
            ThreadAssist.ProxyToMain (delegate {
                DaapSource source = new DaapSource (args.Service);
                string key = String.Format ("{0}:{1}", args.Service.Name, args.Service.Port);

                if (source_map.Count == 0) {
                    ServiceManager.SourceManager.AddSource (container);
                }

                if (source_map.ContainsKey (key)) {
                    // Received new connection info for service
                    container.RemoveChildSource (source_map [key]);
                    source_map [key] = source;
                } else {
                    // New service information
                    source_map.Add (key, source);
                }

                container.AddChildSource (source);

                // Don't flash shares we find on startup (well, within 5s of startup)
                if ((DateTime.Now - locator_started).TotalSeconds > 5) {
                    source.NotifyUser ();
                }
            });
        }

        private void OnServiceRemoved (object o, ServiceArgs args)
        {
            ThreadAssist.ProxyToMain (delegate {
                string key = String.Format ("{0}:{1}", args.Service.Name, args.Service.Port);
                DaapSource source = source_map [key];

                source.Disconnect (true);
                container.RemoveChildSource (source);
                source_map.Remove (key);

                if (source_map.Count == 0) {
                    ServiceManager.SourceManager.RemoveSource (container);
                }
            });
        }

        public void DelayedInitialize ()
        {
            Application.RunTimeout (3*1000, DelayedInitializeTimeout);
        }

        public bool DelayedInitializeTimeout ()
        {
            // Add the source, even though its empty, so that the user sees the
            // plugin is enabled, just no child sources yet.
            source_map = new Dictionary<string, DaapSource> ();
            container = new DaapContainerSource ();

            try {
                // Now start looking for services.
                // We do this after creating the source because if we do it before
                // there's a race condition where we get a service before the source
                // is added.
                locator = new ServiceLocator ();
                locator.Found += OnServiceFound;
                locator.Removed += OnServiceRemoved;
                locator.ShowLocalServices = true;
                locator_started = DateTime.Now;
                locator.Start ();

                proxy_server = new DaapProxyWebServer ();
                proxy_server.Start ();
            } catch (Exception e) {
                Hyena.Log.Exception ("Failed to start DAAP client", e);
            }
            return false;
        }

        string IService.ServiceName {
            get { return "DaapService"; }
        }
    }
}
