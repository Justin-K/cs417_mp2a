using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class BuildingGrid : MonoBehaviour {
    
    public class BuildingSlot {
        public int index;
        public GameObject slotObject;
        public GameObject placedBuilding;
        public BuildingSlot(int index, GameObject slotObject) {
            this.index = index;
            this.slotObject = slotObject;
        }
    }

    public List<BuildingSlot> building_grid = new List<BuildingSlot>();
    public GameObject grid_frame;
    public GameController controller;

    void Awake() {
        if (grid_frame == null || controller == null) {
            Debug.LogError("BuildingGrid: Assign Grid Frame and Controller in Inspector.", this);
            return;
        }
        building_grid.Clear();
        
        XRSimpleInteractable[] interactables = grid_frame.GetComponentsInChildren<XRSimpleInteractable>(true);
        foreach (XRSimpleInteractable interactable in interactables) {
            building_grid.Add(new BuildingSlot(building_grid.Count, interactable.gameObject));
            interactable.selectEntered.AddListener(controller.OnGridSlotSelect);
        }
        // Debug.Log($"BuildingGrid: Registered {building_grid.Count} slots.", this);
    }

    void OnDestroy() {
        if (grid_frame == null || controller == null) return;
        foreach (BuildingSlot slot in building_grid) {
            if (slot.slotObject == null) continue;
            XRSimpleInteractable interactable = slot.slotObject.GetComponent<XRSimpleInteractable>();
            if (interactable != null) interactable.selectEntered.RemoveListener(controller.OnGridSlotSelect);
        }
    }

    public BuildingSlot FindSlot(Transform selected) {
        foreach (BuildingSlot slot in building_grid) {
            if (slot.slotObject != null && slot.slotObject.transform == selected) return slot;
        }
        return null;
    }

    void Update() { }
}
