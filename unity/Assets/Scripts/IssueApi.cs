using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Inspection
{
    public static class IssueApi
    {
        public static IEnumerator Submit(string baseUrl, CreateIssueRequest payload,
            Action<IssueResponse> success, Action<string> failure)
        {
            using (var request = new UnityWebRequest(baseUrl.TrimEnd('/') + "/api/issues", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 15;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string message = null;
                    try { message = JsonUtility.FromJson<ApiErrorResponse>(request.downloadHandler.text)?.error?.message; }
                    catch (Exception) { /* Network errors may have no JSON body. */ }
                    failure(message ?? $"Request failed ({request.responseCode}): {request.error}. Check address and network.");
                    yield break;
                }

                IssueResponse issue = null;
                try { issue = JsonUtility.FromJson<IssueResponse>(request.downloadHandler.text); }
                catch (Exception) { /* Handled below; preserve the user's draft. */ }
                if (issue == null || string.IsNullOrEmpty(issue.id))
                    failure("Invalid server response. Draft retained; check the web list before retrying.");
                else success(issue);
            }
        }
    }
}
