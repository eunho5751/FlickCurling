using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FlickCurling.Game.Online
{
    /// <summary>
    /// Where the match is, as one player's device last had it at a steady point (a turn not yet thrown, or a card):
    /// the flow (<c>Phase</c>), the turn's deadline, the match state (MatchState), and when it was written (network time).
    /// While that turn's shot is playing, the shot too (<c>ShotSeq</c> is the turn, -1 for none): its input, its record
    /// and when its playback started, so a device can show it at the same point.
    /// </summary>
    public sealed class MatchSnapshot : INetworkSerializable, IEquatable<MatchSnapshot>
    {
        public double Stamp;
        public int Phase;
        public double Deadline;
        public byte[] Data = Array.Empty<byte>();
        public int ShotSeq = -1;
        public double ShotAng, ShotPw, ShotLane, ShotStart;
        public byte[] Shot = Array.Empty<byte>();

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Stamp);
            s.SerializeValue(ref Phase);
            s.SerializeValue(ref Deadline);
            if (s.IsWriter && Data == null) Data = Array.Empty<byte>();
            s.SerializeValue(ref Data);
            s.SerializeValue(ref ShotSeq);
            s.SerializeValue(ref ShotAng); s.SerializeValue(ref ShotPw); s.SerializeValue(ref ShotLane); s.SerializeValue(ref ShotStart);
            if (s.IsWriter && Shot == null) Shot = Array.Empty<byte>();
            s.SerializeValue(ref Shot);
        }

        public bool Equals(MatchSnapshot o) => o != null && Stamp == o.Stamp && Phase == o.Phase && Deadline == o.Deadline && ShotSeq == o.ShotSeq
            && (Data?.Length ?? 0) == (o.Data?.Length ?? 0);
        public override bool Equals(object o) => Equals(o as MatchSnapshot);
        public override int GetHashCode() => Stamp.GetHashCode();
    }

    /// <summary>
    /// One player's networked card in an online match (Resources/OnlinePlayerState): each device spawns and owns its own
    /// on joining the match's session, and keeps there who it is (id, nickname, record) and where the match is
    /// (<see cref="State"/>, rewritten at every steady point and as a shot starts). The Distributed Authority service keeps
    /// these values, so a device back from the background takes the match from the other's at once. It goes with its
    /// owner's device (dropped from the session, its card goes too: that is how the other learns it is gone).
    /// </summary>
    public sealed class OnlinePlayerState : NetworkBehaviour
    {
        public const string ResourcePath = "OnlinePlayerState";

        public readonly NetworkVariable<FixedString64Bytes> PlayerId = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<FixedString128Bytes> Nick = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> HasStats = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<int> W = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<int> L = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<MatchSnapshot> State = new(new MatchSnapshot(), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        static readonly List<OnlinePlayerState> all = new List<OnlinePlayerState>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => all.Clear();

        public override void OnNetworkSpawn()
        {
            all.Add(this);
            DontDestroyOnLoad(gameObject);   // the match goes from the main menu into the game scene
            if (!IsOwner) return;
            PlayerId.Value = OnlineLobby.MyId ?? "";
            Nick.Value = Truncate(OnlineLobby.MyName);
            var s = OnlineLobby.MyStats;
            HasStats.Value = s != null;
            if (s != null) { W.Value = s.W; L.Value = s.L; }
        }

        public override void OnNetworkDespawn() => all.Remove(this);

        // (a nickname too long for the field is cut, on a character)
        static FixedString128Bytes Truncate(string s)
        {
            s ??= "";
            var f = new FixedString128Bytes();
            foreach (var ch in s) if (f.Append(ch) != FormatError.None) break;
            return f;
        }

        /// <summary>This device's own card (null until spawned).</summary>
        public static OnlinePlayerState Mine
        {
            get { foreach (var p in all) if (p && p.IsSpawned && p.IsOwner) return p; return null; }
        }

        /// <summary>Player <paramref name="playerId"/>'s card on another device, the latest written if there are more
        /// (one left behind by a device that came back); null if none.</summary>
        public static OnlinePlayerState Of(string playerId)
        {
            OnlinePlayerState best = null;
            foreach (var p in all)
            {
                if (!p || !p.IsSpawned || p.IsOwner || p.PlayerId.Value.ToString() != playerId) continue;
                if (best == null || (p.State.Value?.Stamp ?? 0) > (best.State.Value?.Stamp ?? 0)) best = p;
            }
            return best;
        }

        /// <summary>Their record as the card has it (null if they had none to give).</summary>
        public OnlineApi.PlayerStats Stats =>
            HasStats.Value ? new OnlineApi.PlayerStats { PlayerId = PlayerId.Value.ToString(), W = W.Value, L = L.Value } : null;

        /// <summary>Writes where the match is on this device's card; returns the stamp (network time), or -1 if there is
        /// no card yet.</summary>
        public static double Publish(int phase, double deadline, byte[] data)
        {
            var me = Mine;
            if (!me) return -1;
            double stamp = OnlineLobby.NetworkTime;
            me.State.Value = new MatchSnapshot { Stamp = stamp, Phase = phase, Deadline = deadline, Data = data ?? Array.Empty<byte>() };
            return stamp;
        }

        /// <summary>Adds the shot of turn <paramref name="seq"/> now playing to what this device's card has (written at that
        /// turn's start); returns the stamp, or -1 if there is no card or nothing on it yet.</summary>
        public static double PublishShot(int seq, double ang, double pw, double lane, double start, byte[] record)
        {
            var me = Mine;
            var at = me ? me.State.Value : null;
            if (at == null || at.Data == null || at.Data.Length == 0) return -1;
            double stamp = OnlineLobby.NetworkTime;
            me.State.Value = new MatchSnapshot
            {
                Stamp = stamp, Phase = at.Phase, Deadline = at.Deadline, Data = at.Data,
                ShotSeq = seq, ShotAng = ang, ShotPw = pw, ShotLane = lane, ShotStart = start, Shot = record ?? Array.Empty<byte>(),
            };
            return stamp;
        }
    }
}
