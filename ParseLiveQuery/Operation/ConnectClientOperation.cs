using System.Collections.Generic;


namespace Parse.LiveQuery; 

public class ConnectClientOperation : SessionClientOperation {

    private readonly string _applicationId;
    private readonly string _clientKey;
    private readonly string _sessionToken;
    internal ConnectClientOperation(string applicationId, string clientKey, string sessionToken) : base(sessionToken)
    {
        _applicationId = applicationId;
        _clientKey = clientKey;
        _sessionToken = sessionToken;
    }


    protected override IDictionary<string, object> ToJsonObject()
    {
        var payload = new Dictionary<string, object>
        {
            ["op"] = "connect",
            ["applicationId"] = _applicationId,
            ["clientKey"] = _clientKey
        };

        if (!string.IsNullOrEmpty(_sessionToken))
        {
            payload["sessionToken"] = _sessionToken;
        }

        return payload;
    }

}
