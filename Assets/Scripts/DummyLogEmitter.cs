using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Randomly prints debug strings. Used for testing that the Log Window is working correctly.
/// </summary>
public class DummyLogEmitter : MonoBehaviour {

    public float MinSeconds = 1.0f;
    public float MaxSeconds = 5.0f;

	// Use this for initialization
	void Start () {
        StartCoroutine(EmitLogStrings());
	}

    private IEnumerator EmitLogStrings()
    {
        int messageNum = 0;
        while(true)
        {
            float waitSeconds = UnityEngine.Random.Range(MinSeconds, MaxSeconds);
            Debug.Log(String.Format("DummyLogEmitter: Test message {0}, waiting for {1} seconds...", messageNum, waitSeconds));
            messageNum++;
            yield return new WaitForSeconds(waitSeconds);
        }
    }

    // Update is called once per frame
    void Update () {
		
	}
}
