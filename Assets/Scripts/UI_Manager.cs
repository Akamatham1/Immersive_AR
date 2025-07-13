using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UI_Manager : MonoBehaviour
{
    // Start is called before the first frame update
    public GameObject UI;
    public GameObject buttons;
    private void Start()
    {
        buttons.SetActive(false);
    }

    public void disable_UI()
    {
        UI.SetActive(false);
        buttons.SetActive(true);
    }
   
}
