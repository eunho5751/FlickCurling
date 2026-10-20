using System;
using Unity.Netcode;
using UnityEngine;

namespace FlickCurling.Game.Online
{
    /// <summary>What the two devices of an online match tell each other (where the match is, each keeps on its own
    /// card instead: <see cref="OnlinePlayerState"/>).</summary>
    public enum OnlineMsg : byte
    {
        Hello,         // here I am (on joining and coming back); Text = my player id
        Ready,         // Seq: I pressed the card (or reached the turn) numbered Seq
        Commit,        // Seq: turn; A, B, C: the shot's angle, power and lane (the shot is thrown, whatever happens next)
        Shot,          // Seq: turn; A, B, C as Commit; Data: its ShotRecord
        Void,          // Seq: turn; the turn ran out of time
        PlaybackDone,  // Seq: turn; I have finished playing the shot
        Ping,          // every second, Text = my player id: I am here (see OnlineLobby.OpponentHere)
        Deadline,      // Seq: turn; A: its deadline in network time (the thrower's device sets it)
        Forfeit,       // Text: the winner's player id (the sender gave up, or claims the win over a long disconnection)
    }

    public struct OnlineMessage
    {
        public OnlineMsg Kind;
        public int Seq;
        public double A, B, C;
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

        float nextPing;

        // the heartbeat (the object lives across scenes, so it keeps beating while a scene loads)
        void Update()
        {
            if (!IsSpawned || Time.realtimeSinceStartup < nextPing) return;
            nextPing = Time.realtimeSinceStartup + 1;
            if (OnlineLobby.InMatch) Send(new OnlineMessage { Kind = OnlineMsg.Ping, Text = OnlineLobby.MyId });
        }

        void OnApplicationPause(bool paused) { if (!paused) OnlineLobby.Resumed(); }

        public void Send(OnlineMessage m) =>
            MessageRpc((byte)m.Kind, m.Seq, m.A, m.B, m.C, m.Data ?? Array.Empty<byte>(), m.Text ?? "");

        [Rpc(SendTo.NotMe)]
        void MessageRpc(byte kind, int seq, double a, double b, double c, byte[] data, string text) =>
            Received?.Invoke(new OnlineMessage { Kind = (OnlineMsg)kind, Seq = seq, A = a, B = b, C = c, Data = data, Text = text });
    }
}
