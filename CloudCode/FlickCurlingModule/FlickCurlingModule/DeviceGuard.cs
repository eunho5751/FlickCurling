using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Core;
using Unity.Services.CloudSave.Model;

namespace FlickCurlingModule;

/// <summary>
/// One device per account: the device that signed in last is the one in use. It is noted in the player's protected
/// data ("device", readable by the player) and the player's other devices are told with a push message, after which
/// they read the note and step aside if it is not theirs.
/// </summary>
public class DeviceGuard
{
    const string DeviceKey = "device";
    const string MessageType = "deviceClaimed";

    readonly IGameApiClient api;
    readonly ILogger<DeviceGuard> log;

    public DeviceGuard(IGameApiClient api, ILogger<DeviceGuard> log)
    {
        this.api = api;
        this.log = log;
    }

    /// <summary>The caller's device is now the one in use; the account's other devices hear about it.</summary>
    [CloudCodeFunction("ClaimDevice")]
    public async Task<bool> ClaimDevice(IExecutionContext ctx, IPushClient push, string deviceId)
    {
        string me = string.IsNullOrEmpty(ctx.PlayerId) ? throw new Exception("missing player") : ctx.PlayerId;
        if (string.IsNullOrEmpty(deviceId)) throw new Exception("missing deviceId");
        await api.CloudSaveData.SetProtectedItemAsync(ctx, ctx.ServiceToken, ctx.ProjectId, me, new SetItemBody(DeviceKey, deviceId, null));
        try { await push.SendPlayerMessageAsync(ctx, deviceId, MessageType, me); }
        catch (Exception e) { log.LogWarning("device push failed: {Error}", e.Message); }   // the others still find out on their next check
        return true;
    }
}
