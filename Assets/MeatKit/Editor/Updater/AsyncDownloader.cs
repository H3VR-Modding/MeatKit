using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.Networking;

namespace MeatKit
{
    [InitializeOnLoad]
    public static class AsyncDownloader
    {
        private static readonly Dictionary<UnityWebRequest, Action<UnityWebRequest>> ActiveRequests = new Dictionary<UnityWebRequest, Action<UnityWebRequest>>();

        static AsyncDownloader()
        {
            EditorApplication.update += Update;
        }

        public static void WaitForCompletion(UnityWebRequest request, Action<UnityWebRequest> callback)
        {
            if (request == null) throw new ArgumentException("Request cannot be null", "request");
            request.Send();
            ActiveRequests.Add(request, callback);
        }
        
        private static void Update()
        {
            foreach (var kv in ActiveRequests.ToArray())
            {
                UnityWebRequest request = kv.Key;
                Action<UnityWebRequest> callback = kv.Value;
                if (!request.isDone) continue;
                if (callback != null) callback(request);
                ActiveRequests.Remove(request);
            }
        }
    }
}