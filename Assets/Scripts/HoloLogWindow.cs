using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class HoloLogWindow : MonoBehaviour {

    public int MaxNumMessages = 16;

    public Text LogText;

    private List<string> Messages = new List<string>();

	// Use this for initialization
	void Start () {
        Application.logMessageReceived += Application_logMessageReceived;

        UpdateMessages();
    }

    private void Application_logMessageReceived(string condition, string stackTrace, LogType type)
    {
        if (condition.Contains("RenderTexture.GenerateMips failed"))
        {
            // Bug in Unity 2017.1 where this error message continually appears. Solution is to wait for a stable release of 2017.2 and migrate.
            // https://forums.hololens.com/discussion/8355/rendertexture-generatemips-failed
            return;
        }


        Messages.Add(condition);
        if (Messages.Count > MaxNumMessages)
        {
            Messages.RemoveAt(0);
        }

        UpdateMessages();
    }

    private void UpdateMessages()
    {
        string messagesString = "";
        for (int i = 0; i < Messages.Count; i++)
        {
            messagesString += Messages[i] + "\n";
        }

        if (LogText != null)
        {
            LogText.text = messagesString;
        }
    }

    // Update is called once per frame
    void Update () {
		
	}
}
