using System;
using System.Net;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // Every dialogue HTTP path shares the same validated local transport policy.
    internal static class LocalDialogueHttp
    {
        internal static HttpWebRequest Create(Uri endpoint)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            var validated = new LocalDialogueConnection(endpoint.AbsoluteUri, 1000);
            var request = (HttpWebRequest)WebRequest.Create(validated.Endpoint);
            request.AllowAutoRedirect = false;
            request.Proxy = null;
            return request;
        }
    }
}
