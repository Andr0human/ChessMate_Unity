using System.IO;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;


public class ArenaDashboard : MonoBehaviour
{
    [SerializeField] private Arena  ar;

    public TextMeshProUGUI GameAmountField;
    public TextMeshProUGUI TimeFormatField;

    private string[] EngineNames;
    private string[] openingFiles;

    public TMP_Dropdown[] DropdownEngines;
    public TMP_Dropdown   DropdownOpening;


    private void
    Start()
    {
        // Get the path to the Streaming Assets folder
        string streamingAssetsPath = Application.streamingAssetsPath;

        // Get the list of all files in the Streaming Assets folder with .exe extension
        string[] exeFilePaths = Directory.GetFiles(streamingAssetsPath, "*.exe");
        EngineNames = new string[exeFilePaths.Length];

        for (int i = 0; i < EngineNames.Length; i++)
            EngineNames[i] = Path.GetFileNameWithoutExtension(exeFilePaths[i]);

        string[] openingFilePaths = Directory.GetFiles(streamingAssetsPath + "/Utility/", "*.opening");
        openingFiles = new string[openingFilePaths.Length];

        for (int i = 0; i < openingFiles.Length; i++)
            openingFiles[i] = Path.GetFileNameWithoutExtension(openingFilePaths[i]);

        PopulateDropdown(DropdownEngines[0], EngineNames);
        PopulateDropdown(DropdownEngines[1], EngineNames);
        PopulateDropdown(DropdownOpening, openingFiles);

        ar.ArenaEngines = new string[2];
    }


    public void
    PopulateDropdown(TMP_Dropdown dropdown, string[] names)
    {
        // Create a list of TMP_Dropdown.OptionData for the dropdown options
        List<TMP_Dropdown.OptionData> dropdownOptions = new List<TMP_Dropdown.OptionData>();
        foreach (string name in names)
        {
            TMP_Dropdown.OptionData optionData = new TMP_Dropdown.OptionData(name);
            dropdownOptions.Add(optionData);
        }

        // Set the dropdown options
        dropdown.options = dropdownOptions;
    }


    public void
    SetSampleSize()
    {
        string text = GameAmountField.text;
        text = RemoveNonAlphaNumeric(text);

        if (text.Length == 0)
            return;

        ar.GamesToPlay = int.Parse(text);
    }


    public void
    SetTimeFormat()
    {
        string[] values = TimeFormatField.text.Split();
        float time_per_side = 60, increment = 0;

        if (values.Length == 0)
            return;

        if (values.Length >= 1)
            time_per_side = float.Parse(RemoveNonAlphaNumeric( values[0] ));

        if (values.Length >= 2)
            increment = float.Parse(RemoveNonAlphaNumeric( values[1] ));

        ar.FixedTimePerGame = time_per_side;
        ar.IncrementPerGame = increment;
    }


    public void
    ArenaStartButton()
    {
        GameObject.Find("BackBoard").SetActive(false);
        GameObject.Find("Game Amount").SetActive(false);
        GameObject.Find("Time Format").SetActive(false);
        GameObject.Find("Openings File").SetActive(false);
        GameObject.Find("Engine 1").SetActive(false);
        GameObject.Find("Engine 2").SetActive(false);
        GameObject.Find("Start Arena Button").SetActive(false);

        ar.ArenaEngines[0]  = EngineNames[DropdownEngines[0].value];
        ar.ArenaEngines[1]  = EngineNames[DropdownEngines[1].value];
        ar.OpeningsFilePath = openingFiles[DropdownOpening.value];

        ar.InitArena();
    }


    public string
    RemoveNonAlphaNumeric(string text)
    {
        string result = string.Empty;
        foreach (char ch in text)
        {
            if (char.IsLetterOrDigit(ch) || (ch == '_') || (ch == '-') || (ch == '.'))
                result += ch;
        }
        return result;
    }


    public void
    ExitButton()
    {
        UnityEngine.Debug.Log("Exit Button called!");
        //! TODO Exit Button (Calls to stop players)
        Application.Quit();
    }

}

