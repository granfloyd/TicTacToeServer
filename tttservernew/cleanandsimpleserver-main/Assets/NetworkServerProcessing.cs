using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text;
using System;
using UnityEditor.Experimental.GraphView;
using Unity.VisualScripting;
using System.Security.Cryptography;
using UnityEditor.PackageManager;
using Unity.Networking.Transport;

public static class Data
{
    public static LinkedList<Account> accounts = new LinkedList<Account>();
}


public class Account
{
    public string username;
    public string password;
    public Account(string u, string p)
    {
        username = u;
        password = p;
    }
}

static public class NetworkServerProcessing
{
    public static int currentPlayerIndex = 0;

    public static char currentPlayerSymbol = 'x';

    //public int usernameSignifier = 0;

    public static int playerCount = 0;

    public static bool bRoomFull = false;
                                                                /// Room name        // Clients IDs 
    public static Dictionary<string, List<int>> roomClients = new Dictionary<string, List<int>>();

    public static string roomName;

    const char sep = ',';

    private const int wrongLoginInfo = 4;
    #region Send and Receive Data Functions

    static public void ReceivedMessageFromClient(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
        //Debug.Log(currentPlayerIndex);
        LoadData();
        Debug.Log("Network msg received =  " + msg + ", from connection id = " + clientConnectionID + ", from pipeline = " + pipeline);

        string[] csv = msg.Split(sep);
        int signifier = int.Parse(csv[0]);

        if (signifier == ClientToServerSignifiers.ChatMSG)
        {
            if (csv.Length < 3)
            {
                Debug.Log("From Server: ERROR... Invalid message format.");
                return;
            }
            string chatusername = csv[1];
            string chattext = csv[2];

            for (int i = 0; i < networkServer.networkConnections.Length; i++)   //send to all connected users
            {
                //1,username,text
                SendMessageToClient(ServerToClientSignifiers.ChatMSG.ToString() + sep + chatusername + sep + chattext, i, TransportPipeline.ReliableAndInOrder);
            }
        }
        else if (signifier == ClientToServerSignifiers.MakeAccount)
        {
            
            // Check if an account with the same username already exists
            foreach (Account account in Data.accounts)
            {
                if (account.username == csv[1])
                {
                    SendMessageToClient(ServerToClientSignifiers.AccountExists.ToString(), clientConnectionID, TransportPipeline.ReliableAndInOrder);
                    return;
                }
            }
            //hash the Account password
            string hashedPassword = Convert.ToBase64String(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(csv[2])));
            Account newAccount = new Account(csv[1], hashedPassword);
            // Add the new account to the accounts list
            Data.accounts.AddLast(newAccount);
            // Save the updated accounts list to disk
            SaveData();
            SendMessageToClient(ServerToClientSignifiers.AccountMade.ToString(), clientConnectionID, TransportPipeline.ReliableAndInOrder);
        }
        else if (signifier == ClientToServerSignifiers.LoginData)
        {
            //unhash the password
            string hashedPassword = Convert.ToBase64String(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(csv[2])));
            // Check if an account with the same username and password exists
            foreach (Account account in Data.accounts)
            {
                if (account.username == csv[1] && account.password == hashedPassword)
                {
                    // If it does, send a success message back to the client and return
                    SendMessageToClient(ServerToClientSignifiers.LoginData.ToString(), clientConnectionID, 
                        TransportPipeline.ReliableAndInOrder);
                    return;
                }
            }
            SendMessageToClient(ServerToClientSignifiers.WrongPasswordOrUsername.ToString(),
                clientConnectionID, TransportPipeline.ReliableAndInOrder);
        }
        else if (signifier == ClientToServerSignifiers.RoomJoin)
        {
            roomName = csv[1];
            if (!roomClients.ContainsKey(roomName))
            {
                roomClients[roomName] = new List<int>();
            }
            roomClients[roomName].Add(clientConnectionID);
            playerCount += 1;
        }
        else if (signifier == ClientToServerSignifiers.RoomExit)
        {
            roomName = csv[1];
            if (roomClients.ContainsKey(roomName))
            {
                roomClients[roomName] = new List<int>();
            }
            roomClients[roomName].Remove(clientConnectionID);
            playerCount -= 1;
        }

        if (!bRoomFull)
        {
            if (playerCount == 2)
            {
                List<int> clientsInRoom = roomClients[roomName]; // Get the clients in the room
                for (int i = 0; i < 2; i++)
                {
                    SendMessageToClient(ServerToClientSignifiers.CreateGame.ToString(), clientsInRoom[i], TransportPipeline.ReliableAndInOrder);
                }

                // Send the "YOUR_TURN" message to the first client in the room
                SendMessageToClient(ServerToClientSignifiers.WhosTurn.ToString(), clientsInRoom[0], TransportPipeline.ReliableAndInOrder);
                currentPlayerIndex = 1;
                bRoomFull = true;
            }
        }
        else if (signifier == ClientToServerSignifiers.DisplayMove)
        {
            currentPlayerIndex++;
            if (currentPlayerIndex >= networkServer.networkConnections.Length)
                currentPlayerIndex = 0;

            // Switch the currentPlayerSymbol to the other player's symbol
            currentPlayerSymbol = currentPlayerSymbol == 'x' ? 'o' : 'x';

            // Send a "MOVE" message to all clients
            string moveMsg = ServerToClientSignifiers.DisplayMove.ToString() + sep +
                csv[1] + sep +
                currentPlayerSymbol.ToString();
            for (int i = 0; i < networkServer.networkConnections.Length; i++)
            {
                SendMessageToClient(moveMsg, i, TransportPipeline.ReliableAndInOrder);
            }

            // Send a "YOUR_TURN" message to the current player
            string turnMsg = ServerToClientSignifiers.WhosTurn.ToString() + sep +
                currentPlayerSymbol.ToString();
            SendMessageToClient(turnMsg, currentPlayerIndex, TransportPipeline.ReliableAndInOrder);

        }
        else if (signifier == ClientToServerSignifiers.Loser)
        {
            List<int> clientsInRoom = roomClients[roomName]; // Get the clients in the room

            SendMessageToClient(ServerToClientSignifiers.WhosTurn.ToString(), clientsInRoom[0], TransportPipeline.ReliableAndInOrder);
            currentPlayerIndex = 1;
        }
        else if (signifier == ClientToServerSignifiers.Winner)
        {
            List<int> clientsInRoom = roomClients[roomName]; // Get the clients in the room

            SendMessageToClient(ServerToClientSignifiers.WhosTurn.ToString(), clientsInRoom[0], TransportPipeline.ReliableAndInOrder);
            currentPlayerIndex = 1;
        }
    }
    static public void SendMessageToClient(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
        networkServer.SendMessageToClient(msg, clientConnectionID, pipeline);
    }

    #endregion

    #region Connection Events

    static public void ConnectionEvent(int clientConnectionID)
    {
        Debug.Log("Client connection, ID == " + clientConnectionID);
    }
    static public void DisconnectionEvent(int clientConnectionID)
    {
        Debug.Log("Client disconnection, ID == " + clientConnectionID);
    }

    #endregion

    #region Account data stuff

    static public void SaveData()
    {
        Debug.Log(Application.persistentDataPath.ToString() + Path.DirectorySeparatorChar + "datafile.txt");
        LinkedList<string> saveData = SerializeAccountData();

        StreamWriter writer = new StreamWriter(Application.persistentDataPath.ToString() + Path.DirectorySeparatorChar + "datafile.txt");
        foreach (string line in saveData)
            writer.WriteLine(line);
        writer.Close();
    }

    static public void LoadData()
    {
        Data.accounts.Clear();
        LinkedList<string> serializedData = new LinkedList<string>();
        string line = "";
        StreamReader sr = new StreamReader(Application.persistentDataPath.ToString() + Path.DirectorySeparatorChar + "datafile.txt");
        { 
            while (!sr.EndOfStream)
            {
                line = sr.ReadLine();
                serializedData.AddLast(line);
            }
        }
        DeserializeSaveData(serializedData);
        sr.Close();
    }

    static private LinkedList<string> SerializeAccountData()
    {
        LinkedList<string> serializedData = new LinkedList<string>();
        foreach (Account account in Data.accounts)
        {
            serializedData.AddLast(account.username + sep + account.password);
        }
        return serializedData;
    }

    static private void DeserializeSaveData(LinkedList<string> serializedData)
    {
        Account account = null;

        foreach (string line in serializedData)
        {
            if (line.Contains(sep))
            {
                string[] csv = line.Split(sep);
                account = new Account(csv[0], csv[1]);
                Data.accounts.AddLast(account);
            }
            else
            {
                Debug.Log("Invalid line in data file: " + line);
            }
        }
    }
    #endregion

    #region Setup
    static NetworkServer networkServer;
    static GameLogic gameLogic;

    static public void SetNetworkServer(NetworkServer NetworkServer)
    {
        networkServer = NetworkServer;
    }
    static public NetworkServer GetNetworkServer()
    {
        return networkServer;
    }
    static public void SetGameLogic(GameLogic GameLogic)
    {
        gameLogic = GameLogic;
    }

    #endregion
}

#region Protocol Signifiers
static public class ClientToServerSignifiers
{
    public const int ChatMSG = 1;
    public const int MakeAccount = 2;
    public const int LoginData = 3;
    public const int AccountExists = 31;
    public const int AccountMade = 32;
    public const int WrongPasswordOrUsername = 34;
    public const int CreateGame = 4;
    public const int WhosTurn = 5;
    public const int DisplayMove = 6;
    public const int FirstStart = 99;
    public const int Restart = 7;
    public const int RoomJoin = 11;
    public const int RoomExit = 12;
    public const int Winner = 21;
    public const int Loser = 22;
}

static public class ServerToClientSignifiers
{
    public const int ChatMSG = 1;
    public const int MakeAccount = 2;
    public const int LoginData = 3;
    public const int AccountExists = 31;
    public const int AccountMade = 32;
    public const int WrongPasswordOrUsername = 34;
    public const int CreateGame = 4;
    public const int WhosTurn = 5;
    public const int DisplayMove = 6;
    public const int FirstStart = 99;
    public const int Restart = 7;
    public const int RoomJoin = 11;
    public const int RoomExit = 12;
    public const int Winner = 21;
    public const int Loser = 22;
}

#endregion

