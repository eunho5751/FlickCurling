using System;
using Unity.Netcode;
using UnityEngine;

namespace FlickCurling.Game.Online
{
    /// <summary>What the two devices of an online match tell each other (where the match is, each keeps on its own
    /// card instead: <see cref="OnlinePlayerState"/>).</summary>
    public enum OnlineMsg : byte
    {
        Ready,         // Seq: I pressed the card (or reached the turn) numbered Seq
        Commit,        // Seq: turn; A, B, C: the shot's angle, power and lane (the shot is thrown, whatever happens next)
        Shot,          // Seq: turn; A, B, C as Commit; T: when its playback started (network time); Data: its ShotRecord
        Void,          // Seq: turn; the turn ran out of time
        PlaybackDone,  // Seq: turn; I have finished playing the shot
        Deadline,      // Seq: turn; A: its deadline in network time (the thrower's device sets it)
        Forfeit,       // Text: the winner's player id (the sender gave up)
    }

    public struct OnlineMessage
    {
        public OnlineMsg Kind;
        public int Seq;
        public double A, B, C, T;
        public byte[] Data;
        public string Text;
    }

    /// <summary>
    /// The one networked object of an online match: carries <see cref="OnlineMessage"/>s from this device to the other.
    /// The session owner spawns it (Resources/OnlineMatchSync); it belongs to whoever owns the session, so it outlives
    /// either player leaving.
    /// </summary>
    public sealed class OnlineMatchSync : NetworkBehaviour
    {
        public const string ResourcePath = "OnlineMatchSync";

        public static OnlineMatchSync Instance { get; private set; }
        public static event Action<OnlineMessage> Received;
        public static event Action Spawned;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);   // the match goes from the main menu into the game scene and back
            Spawned?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        public void Send(OnlineMessage m) =>
            MessageRpc((byte)m.Kind, m.Seq, m.A, m.B, m.C, m.T, m.Data ?? Array.Empty<byte>(), m.Text ?? "");

        [Rpc(SendTo.NotMe)]
        void MessageRpc(byte kind, int seq, double a, double b, double c, double t, byte[] data, string text) =>
            Received?.Invoke(new OnlineMessage { Kind = (OnlineMsg)kind, Seq = seq, A = a, B = b, C = c, T = t, Data = data, Text = text });
    }
}
