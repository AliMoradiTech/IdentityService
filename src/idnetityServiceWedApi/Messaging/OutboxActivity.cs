using System.Diagnostics;

namespace idnetityServiceWedApi.Messaging;

public static class OutboxActivity
{
    public static readonly ActivitySource Source = new("IdentityService.Outbox");

    public static Activity? StartProducerActivity(string? traceParent, string? traceState)
    {
        if (traceParent is not null &&
            ActivityContext.TryParse(traceParent, traceState, out ActivityContext parentContext))
        {
            return Source.StartActivity("publish user-registered", ActivityKind.Producer, parentContext);
        }

        return Source.StartActivity("publish user-registered", ActivityKind.Producer);
    }
}
