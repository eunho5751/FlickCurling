using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FlickCurling.Game.Online
{
    /// <summary>One leaderboard row (built by MainMenuBuilder): the rank, a medal instead for the first three, the nickname,
    /// the rating points and the win rate, on its plate; this player's row on the highlighted plate.</summary>
    public sealed class LeaderboardRow : MonoBehaviour
    {
        [SerializeField] Image plate, medal;
        [SerializeField] TextMeshProUGUI rank, nickname, points, winRate;
        [SerializeField] Sprite plateSprite, mineSprite;
        [SerializeField] Sprite[] medals = new Sprite[3];   // gold, silver, bronze
        [SerializeField] Color ink = Color.black, mineInk = Color.black;

        public void Set(int place, string name, int score, string rate, bool isMine)
        {
            plate.sprite = isMine ? mineSprite : plateSprite;
            bool medalled = place >= 1 && place <= medals.Length && medals[place - 1];
            medal.gameObject.SetActive(medalled);
            if (medalled) medal.sprite = medals[place - 1];
            rank.gameObject.SetActive(!medalled);
            rank.text = place.ToString();
            nickname.text = name;
            points.text = score.ToString("N0");
            winRate.text = rate;
            foreach (var t in new[] { rank, nickname, points, winRate }) t.color = isMine ? mineInk : ink;
        }
    }
}
