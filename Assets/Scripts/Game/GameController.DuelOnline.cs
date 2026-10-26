using System;
using FlickCurling.Core;
using FlickCurling.Game.Online;
using UnityEngine;

namespace FlickCurling.Game
{
    // The turn order shot thrown together, online: this device's draw goes to the other (a message, and onto this
    // device's card for one that missed it), and theirs comes the same way.
    public sealed partial class GameController
    {
        const int LsdDuelSeq = -2;   // the card's shot "turn" for the draw

        void OnlineDuelSend(LsdThrow t)
        {
            if (!IsOnline) return;
            var bytes = t.Record != null ? t.Record.ToBytes() : Array.Empty<byte>();
            OnlineSend(OnlineMsg.LsdThrow, a: t.Ang, b: t.Pw, c: t.Lane, data: bytes);
            OnlinePlayerState.PublishShot(LsdDuelSeq, t.Ang, t.Pw, t.Lane, 0, bytes);
        }

        // the other side's draw: its record (none: out of time) read here, so both devices have the same result
        void DuelReceive(double ang, double pw, double lane, byte[] data)
        {
            if (!m.InLsd || !LsdDuel || duelOppThrow != null) return;
            var team = Sheet.Other(duelMine);
            ShotRecord rec = null;
            if (data != null && data.Length > 0)
            {
                try { rec = ShotRecord.FromBytes(data); }
                catch (Exception e) { Debug.LogWarning("FlickCurling: bad draw record: " + e.Message); return; }
            }
            DuelTheirs(rec != null ? m.LsdFromRecord(team, ang, pw, lane, rec) : NoRecord(team));
        }

        // a draw message missed (the background): it is on their card too
        void TickDuelCard()
        {
            if (!LsdDuel || !m.InLsd || duelOppThrow != null || (state != St.Aim && state != St.LsdDuel)) return;
            var card = OnlinePlayerState.Of(OnlineLobby.OpponentId);
            var snap = card ? card.State.Value : null;
            if (snap == null || snap.ShotSeq != LsdDuelSeq) return;
            DuelReceive(snap.ShotAng, snap.ShotPw, snap.ShotLane, snap.Shot);
        }
    }
}
