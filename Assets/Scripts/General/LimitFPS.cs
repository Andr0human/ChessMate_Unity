using UnityEngine;

public class LimitFPS : MonoBehaviour
{
    void Start()
    {
        Application.targetFrameRate = 60;
    }
}
