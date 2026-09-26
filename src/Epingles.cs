using System.Text.Json;
using Vintagestory.API.Server;

namespace CurveoStockage;

/// <summary>Objets épinglés par chaque joueur (clés d'objet), communs à tous ses terminaux et tablettes, sauvegardés avec la partie.</summary>
public class GestionEpingles
{
    private const string Cle = "curveostockage-epingles";
    public const int Max = 9;
    private readonly ICoreServerAPI sapi;
    private Dictionary<string, List<string>> parJoueur = new();

    public GestionEpingles(ICoreServerAPI sapi)
    {
        this.sapi = sapi;
        sapi.Event.SaveGameLoaded += () =>
        {
            var d = sapi.WorldManager.SaveGame.GetData(Cle);
            if (d != null) try { parJoueur = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(d) ?? new(); } catch { parJoueur = new(); }
        };
        sapi.Event.GameWorldSave += () => sapi.WorldManager.SaveGame.StoreData(Cle, JsonSerializer.SerializeToUtf8Bytes(parJoueur));
    }

    public List<string> De(string uid) => parJoueur.TryGetValue(uid, out var l) ? l : new List<string>();

    public void Basculer(string uid, string cle)
    {
        if (!parJoueur.TryGetValue(uid, out var l)) parJoueur[uid] = l = new List<string>();
        if (!l.Remove(cle) && l.Count < Max) l.Add(cle);
    }
}
