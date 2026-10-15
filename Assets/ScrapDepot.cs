using UnityEngine;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
public class ScrapDepot : MonoBehaviour {
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public int regular_scrap_count;
    public int electronic_scrap_count;
    public GameController controller;
    public GameObject selectable;
    public TextMeshPro status_text;
    public TextMeshPro tutorial_text;
    public void UpdateStatusText() { status_text.SetText($"Scrap Depot\nRegular: {regular_scrap_count}\nElectronic: {electronic_scrap_count}"); }
    void Start() {
        regular_scrap_count = 0;
        electronic_scrap_count = 0;
        selectable.GetComponent<XRSimpleInteractable>().selectEntered.AddListener(controller.OnDepotSelect);
        UpdateStatusText();
    }

    // Update is called once per frame
    void Update() {
        
    }
}
