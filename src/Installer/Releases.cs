using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Web.Script.Serialization;

namespace Morta4P.Installer
{
    // Finds the newest Morta4P.dll in the GitHub releases of the project. Pre-releases count:
    // every version so far is published as one, and /releases/latest would skip them.
    internal static class Releases
    {
        private const string ApiUrl = "https://api.github.com/repos/williamanjo/Children_of_Morta4P/releases?per_page=20";
        private const string AssetName = "Morta4P.dll";

        internal sealed class Release
        {
            public string Tag;
            public string DllUrl;
        }

        internal static Release FindLatest()
        {
            string json;
            using (WebClient client = CreateClient())
            {
                client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                json = client.DownloadString(ApiUrl);
            }

            var releases = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(json);
            // The API lists releases newest first.
            foreach (var release in releases)
            {
                if (release.TryGetValue("draft", out var draft) && draft is bool isDraft && isDraft)
                {
                    continue;
                }
                if (!(release.TryGetValue("assets", out var assets) && assets is IEnumerable list))
                {
                    continue;
                }
                foreach (var item in list)
                {
                    if (item is Dictionary<string, object> asset
                        && string.Equals(asset["name"] as string, AssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        return new Release
                        {
                            Tag = release["tag_name"] as string,
                            DllUrl = asset["browser_download_url"] as string,
                        };
                    }
                }
            }
            return null;
        }

        internal static byte[] Download(string url)
        {
            using (WebClient client = CreateClient())
            {
                return client.DownloadData(url);
            }
        }

        private static WebClient CreateClient()
        {
            // .NET Framework defaults can leave TLS 1.2 off; GitHub requires it.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "Morta4P-Installer";
            return client;
        }
    }
}
