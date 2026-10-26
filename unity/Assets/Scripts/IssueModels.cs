using System;

namespace Inspection
{
    [Serializable]
    public class IssuePosition { public float x; public float y; public float z; }

    [Serializable]
    public class CreateIssueRequest
    {
        public string title;
        public string description;
        public string priority;
        public IssuePosition position;
    }

    [Serializable]
    public class IssueResponse
    {
        public string id;
        public string title;
        public string description;
        public string priority;
        public string status;
        public IssuePosition position;
        public string createdAt;
        public string updatedAt;
    }

    [Serializable]
    public class ApiError { public string code; public string message; }

    [Serializable]
    public class ApiErrorResponse { public ApiError error; }
}
