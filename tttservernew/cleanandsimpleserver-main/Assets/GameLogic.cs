using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameLogic : MonoBehaviour
{
    //public int player1 = -1;
    //public int player2 = -1;
    void Start()
    {
        NetworkServerProcessing.SetGameLogic(this);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
            NetworkServerProcessing.SendMessageToClient("2,Hello client's world, sincerely your network server", 0, TransportPipeline.ReliableAndInOrder);
    }

}
