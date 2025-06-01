using Parse.Infrastructure.Utilities;
using System.Collections.Generic;

namespace Parse.LiveQuery;
public class UnsubscribeClientOperation : IClientOperation {

    private readonly int _requestId;

    internal UnsubscribeClientOperation(int requestId) {
        _requestId = requestId;
    }

    public string ToJson()
    {
        return JsonUtilities.Encode(new Dictionary<string, object>
        {
            ["op"] = "unsubscribe",
            ["requestId"] = _requestId
        });
    }
}
