using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Text;
using System;
using System.Security.Cryptography;

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
    public static string moveMsg = "";

    public static string roomName;

    public static List<string> createdRooms = new List<string>();//rooms

    public static Dictionary<int, string> clientRooms = new Dictionary<int, string>();

    public static Dictionary<string, List<int>> roomClients = new Dictionary<string, List<int>>();//clients

    public static Dictionary<string, int[]> roomPlayers = new Dictionary<string, int[]>();//players

    public static Dictionary<string, List<string>> roomGameStates = new Dictionary<string, List<string>>();

    const char sep = ',';
    const int maxPlayerCount = 3;
    #region Send and Receive Data Functions

    static public void ReceivedMessageFromClient(string msg, int clientConnectionID, TransportPipeline pipeline)
    {
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
                SendMessageToClient(ServerToClientSignifiers.GlobalChatMSG.ToString() + sep + chatusername + sep + chattext, i, TransportPipeline.ReliableAndInOrder);
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
                roomClients[roomName] = new List<int>();//clients in that room
                roomPlayers[roomName] = new int[2] { -1, -1 };//player1 and player2
                createdRooms.Add(roomName); //add room name to list 
            }
            List<int> clientsInRoom = roomClients[roomName]; // Get the clients in the room
            roomClients[roomName].Add(clientConnectionID);
            clientRooms[clientConnectionID] = roomName; // client in room

            if (clientsInRoom.Count < maxPlayerCount)//if less then 2 people are in room
            {
                if (roomPlayers[roomName][0] == -1)
                {
                    roomPlayers[roomName][0] = clientConnectionID;
                }
                else if (roomPlayers[roomName][1] == -1)
                {
                    roomPlayers[roomName][1] = clientConnectionID;
                }
            }
            if (clientsInRoom.Count < maxPlayerCount && roomPlayers[roomName][0] >= 0 && roomPlayers[roomName][1] >= 0)//only sends if atleast 2 ppl in lobby and player1 and player2 are assined 
            {
                SendMessageToClient(ServerToClientSignifiers.CreateGame.ToString(), roomPlayers[roomName][0], TransportPipeline.ReliableAndInOrder);
                SendMessageToClient(ServerToClientSignifiers.CreateGame.ToString(), roomPlayers[roomName][1], TransportPipeline.ReliableAndInOrder);

                // Send the "YOUR_TURN" message to the first client in the room
                SendMessageToClient(ServerToClientSignifiers.CurrentTurn.ToString(), roomPlayers[roomName][0], TransportPipeline.ReliableAndInOrder);
            }
            else if (clientsInRoom.Count >= maxPlayerCount)
            {
                SendMessageToClient(ServerToClientSignifiers.RoomSpectate.ToString(), clientConnectionID, TransportPipeline.ReliableAndInOrder);
                if (roomGameStates.ContainsKey(roomName))
                {
                    foreach (string moveMsg in roomGameStates[roomName])
                    {
                        SendMessageToClient(moveMsg, clientConnectionID, TransportPipeline.ReliableAndInOrder);
                    }
                }
                Debug.Log("We got a spectater ");
            }
            if (roomGameStates.ContainsKey(roomName))
            {
                foreach (string moveMsg in roomGameStates[roomName])
                {
                    SendMessageToClient(moveMsg, clientConnectionID, TransportPipeline.ReliableAndInOrder);
                    Debug.Log("welcomeback");
                }
            }

        }
        else if (signifier == ClientToServerSignifiers.RoomExit)
        {
            roomName = clientRooms[clientConnectionID]; //roomname from client
            if (roomClients.ContainsKey(roomName))
            {
                roomClients[roomName].Remove(clientConnectionID);
                clientRooms.Remove(clientConnectionID);
                if (roomPlayers[roomName][0] == clientConnectionID)
                {
                    roomPlayers[roomName][0] = -1;
                }
                else if (roomPlayers[roomName][1] == clientConnectionID)
                {
                    roomPlayers[roomName][1] = -1;
                }
            }

            if (roomClients[roomName].Count == 0)//if no one is in the room remove it
            {
                roomClients.Remove(roomName);
                roomPlayers.Remove(roomName);
                createdRooms.Remove(roomName);
            }
        }

        else if (signifier == ClientToServerSignifiers.SendMove)
        {
            string roomName = clientRooms[clientConnectionID];
            if (createdRooms.Contains(roomName))
            {
                int[] playersInRoom = roomPlayers[roomName];
                int currentPlayerIndex = Array.IndexOf(playersInRoom, clientConnectionID);
                char currentPlayerSymbol = currentPlayerIndex == 0 ? 'o' : 'x';
                currentPlayerIndex = currentPlayerIndex == 0 ? 1 : 0;

                // Send a "MOVE" message to all clients in the room
                string moveMsg = ServerToClientSignifiers.DisplayMove.ToString() + sep +
                    csv[1] + sep +
                    currentPlayerSymbol.ToString();
                foreach (int clientID in roomClients[roomName])
                {
                    SendMessageToClient(moveMsg, clientID, TransportPipeline.ReliableAndInOrder);
                }

                foreach (int playerID in roomPlayers[roomName])
                {
                    SendMessageToClient(moveMsg, playerID, TransportPipeline.ReliableAndInOrder);
                }
                //adds move to rooms game
                if (!roomGameStates.ContainsKey(roomName))
                {
                    roomGameStates[roomName] = new List<string>();
                }
                roomGameStates[roomName].Add(moveMsg);

                // Send a message to the player your turn
                string turnMsg = ServerToClientSignifiers.CurrentTurn.ToString() + sep +
                    currentPlayerSymbol.ToString();
                SendMessageToClient(turnMsg, playersInRoom[currentPlayerIndex], TransportPipeline.ReliableAndInOrder);
            }
        }
        else if (signifier == ClientToServerSignifiers.ClearBoard)
        {
            string roomName = clientRooms[clientConnectionID];// Get the room name
            List<int> clientsInRoom = roomClients[roomName];// Get the clients in the room

            foreach (int clientID in clientsInRoom)
            {
                SendMessageToClient(ServerToClientSignifiers.ClearedBoard.ToString(), clientID, TransportPipeline.ReliableAndInOrder);
            }
            SendMessageToClient(ServerToClientSignifiers.CurrentTurn.ToString(), roomPlayers[roomName][0], TransportPipeline.ReliableAndInOrder);
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
    public const int ChatMSG = 10; //sends server chat msg 

    public const int MakeAccount = 20;   //sends server client login data for new account
    public const int LoginData = 21;     //sends server client login data only for made accounts

    public const int SendMove = 30;      //send move to server
    public const int ClearBoard = 31;        //new

    public const int RoomJoin = 40;
    public const int RoomSpectate = 41;
    public const int RoomExit = 42;

}

static public class ServerToClientSignifiers
{
    public const int GlobalChatMSG = 10;

    public const int LoginData = 20;
    public const int AccountExists = 21;
    public const int AccountMade = 22;
    public const int WrongPasswordOrUsername = 23;

    public const int CreateGame = 30;
    public const int CurrentTurn = 31;
    public const int DisplayMove = 32;
    public const int ClearedBoard = 33;

    public const int RoomSpectate = 40;

}
#endregion

