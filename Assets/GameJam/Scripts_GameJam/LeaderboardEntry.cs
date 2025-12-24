using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LeaderboardEntry : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI rankText;  // "1.", "2." yazacak yer
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private Image background;         // Vurgu için opsiyonel arka plan

    public void Setup(int rank, string pName, int score, bool isLocal)
    {
        rankText.text = rank.ToString() + ".";
        nameText.text = pName;
        scoreText.text = score.ToString();

        if (isLocal)
        {
            // Kendini vurgula: Sarı ve Kalın yazı
            nameText.color = Color.yellow;
            nameText.fontStyle = FontStyles.Bold;
            if (background != null) background.color = new Color(1, 1, 0, 0.3f);
        }
        else
        {
            // Diğerleri: Beyaz ve Normal yazı
            nameText.color = Color.white;
            nameText.fontStyle = FontStyles.Normal;
            if (background != null) background.color = new Color(0, 0, 0, 0.5f);
        }
    }
}
