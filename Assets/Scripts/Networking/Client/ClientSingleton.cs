using System.Threading.Tasks;
using UnityEngine;

public class ClientSingleton : MonoBehaviour
{
    public ClientGameManager GameManager { get; private set; }
    private static ClientSingleton instance;
    public static ClientSingleton Instance
    {
        get
        {
            if(instance != null)
            {
                return instance;
            }
            instance = FindFirstObjectByType<ClientSingleton>();
            
            if (instance == null)
            {
                Debug.LogError("No ClientSingleton instance found in the scene.");
                return null;
            }
            return instance;
            //...
        }
    }
    public async Task<bool> CreateClient()
    {
        GameManager = new ClientGameManager();
        bool success = await GameManager.InitAsync();
        return success;
    }


    void Start()
    {
        DontDestroyOnLoad(gameObject);
    }
}
