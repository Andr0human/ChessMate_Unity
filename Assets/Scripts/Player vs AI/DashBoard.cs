using UnityEngine;
using UnityEngine.UI;

public class DashBoard : MonoBehaviour
{
    [SerializeField] private Timer tmr;
    [SerializeField] private GameObject[] ChessClocksText;
    [SerializeField] private GameObject BookMissingBanner;

    public int TimeOption;
    public int SideOption;
    public float FixedTime, IncTime;


    private void
    Start()
    {
        OpeningBook ob = GameObject.FindAnyObjectByType<OpeningBook>();
        bool bookLoaded = (ob != null) && (ob.Book != null) && (ob.Book.Count > 0);

        if (BookMissingBanner != null)
            BookMissingBanner.SetActive(!bookLoaded);
    }


    public void
    TimeDropDownMenu(int option)
    {
        TimeOption = option;

        if (option == 0)
            return;
        
        if (option == 1)
        {
            FixedTime = 180;
            IncTime = 2;
        }
        else if (option == 2)
        {
            FixedTime = 60;
            IncTime = 1;
        }
        else if (option == 3)
        {
            FixedTime = 600;
            IncTime = 5;
        }
        else
        {
            //! TODO for custom time format
        }
    }


    public void
    SideDropDownMenu(int option)
    {
        SideOption = (option == 2) ? Random.Range(0, 2) : option;
    }


    public void
    PlayButton()
    {
        GameObject.Find("Play Button").SetActive(false);
        GameObject.Find("Time Format").SetActive(false);
        GameObject.Find("Color Format").SetActive(false);
        GameObject.Find("BackBoard").SetActive(false);

        if (BookMissingBanner != null)
            BookMissingBanner.SetActive(false);

        if (TimeOption == 0) {}
            // tmr.enabled = false;
        else
        {
            tmr.SetTime(FixedTime, IncTime);

            ChessClocksText[0].SetActive(true);
            ChessClocksText[1].SetActive(true);
        }


        string player_white = (SideOption == 0) ? "human" : "bot";
        string player_black = (SideOption == 1) ? "human" : "bot";

        //! TODO code <FixedMoveTime>
        StartCoroutine(
            GameObject.FindAnyObjectByType<MatchManager>().StartNewGame(
                player_white, player_black, "",
                false, true
            )
        );
    }


    public void
    ExitButton()
    {
        Application.Quit();
    }


    //! TODO Flip board to play from black side
}

