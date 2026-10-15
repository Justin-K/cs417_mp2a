using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.InputSystem;
using TMPro;
/*
Things the `GameController` should have/do:
- Generate the `Maze`
    - This way the `GameController` can:
        + Spawn `Landmine`s
        + Have access to `Maze` cells for bot paths

- All relevant `Maze` generation parameters

- `Landmine` generation parameters
    + Density/frequency/count
    + `Landmine` damage

- Relevant `Artifact`/goal placement parameters
    + min distance from center

- Placement grid for spawned buildings

- `MainTerminal` for info display to the player

- Player stats
    - `carried_scrap`
    - `carry_limit`

*/

public class GameController : MonoBehaviour {
    private int carried_regular_scrap;
    private int carried_electronic_scrap;
    public int carry_capacity;
    // public GameObject landmine_prefab;
    // public GameObject maze;
    public Elevator elevator;
    public ScrapDepot starting_scrap_depot;
    public BuildingGrid building_grid;
    private SharedLibrary.PlaceableBuildingType[] building_types = {
        SharedLibrary.PlaceableBuildingType.ScrapDepot,
        SharedLibrary.PlaceableBuildingType.Fabricator,
        SharedLibrary.PlaceableBuildingType.LaunchBay
    };
    private int[,] building_costs = {
        {1, 2},
        {2, 2},
        {3, 3}
    };
    private string bstr;
    public GameObject[] building_prefabs;
    public int current_building_index;
    public TextMeshPro player_inv_text;
    public TextMeshPro player_build_text;
    public InputActionReference build_mode_action;
    public InputActionReference cycle_building_selection_action;
    public NearFarInteractor right_interactor;
    public TextMeshPro build_mode_tutorial_text;
    private bool build_mode;
    private bool scrap_depot_tutorial;
    private bool build_mode_tutorial;

    public void UpdateInvText() { player_inv_text.SetText($"Capacity: {carry_capacity}\nReg. Scrap: {carried_regular_scrap}\nElec. Scrap {carried_electronic_scrap}"); }
    public void UpdateBuildText() {
        string new_str = (!build_mode) ? $"Build Mode: Off" : $"BuildMode: On\nSelection: {bstr}\nCost: {building_costs[current_building_index, 0]} Reg., {building_costs[current_building_index, 1]} Elec.";
        player_build_text.SetText(new_str);
    }
    public void OnBuildModeEnter() {
        if (build_mode) { return; }
        build_mode = true;
        right_interactor.interactionLayers = InteractionLayerMask.GetMask("BuildingSlot");
        UpdateBuildText();
        build_mode_tutorial = false;
        build_mode_tutorial_text.enabled = false;
    }
    public void OnBuildModeExit() {
        if (!build_mode) { return; }
        build_mode = false;
        right_interactor.interactionLayers = InteractionLayerMask.GetMask("Default");
        UpdateBuildText();
    }
    public void OnBuildingSelectionCycle() {
        if (!build_mode) { return; }
        current_building_index = (current_building_index + 1) % building_types.Length;
        switch (building_types[current_building_index]) {
            case SharedLibrary.PlaceableBuildingType.ScrapDepot: { bstr = "Scrap Depot"; break; }
            case SharedLibrary.PlaceableBuildingType.Fabricator: { bstr = "Fabricator"; break; }
            case SharedLibrary.PlaceableBuildingType.LaunchBay: { bstr = "Launch Bay"; break; }
        }
        UpdateBuildText();

    }
    public void OnGridSlotSelect(SelectEnterEventArgs args) {
        if (!build_mode || building_grid == null || args == null || args.interactableObject == null) return;
        BuildingGrid.BuildingSlot slot = building_grid.FindSlot(args.interactableObject.transform);
        if (slot == null || slot.placedBuilding != null) return;
        if (building_prefabs == null || current_building_index >= building_prefabs.Length ||
            building_prefabs[current_building_index] == null) {
            Debug.LogWarning("GameController: Missing building prefab for current selection.");
            return;
        }
        bool fee_paid = false;
        if (starting_scrap_depot.electronic_scrap_count >= building_costs[current_building_index, 1]
            && starting_scrap_depot.regular_scrap_count >= building_costs[current_building_index, 0]) {
            starting_scrap_depot.electronic_scrap_count -= building_costs[current_building_index, 1];
            starting_scrap_depot.regular_scrap_count -= building_costs[current_building_index, 0];
            fee_paid = true;
            starting_scrap_depot.UpdateStatusText();

        } else if (carried_electronic_scrap >= building_costs[current_building_index, 1]
            && carried_regular_scrap >= building_costs[current_building_index, 0]) {
            carried_electronic_scrap -= building_costs[current_building_index, 1];
            carried_regular_scrap -= building_costs[current_building_index, 0];
            fee_paid = true;
            UpdateInvText();
        }
        if (fee_paid) {
           slot.placedBuilding = Instantiate(building_prefabs[current_building_index], slot.slotObject.transform);
            if (building_types[current_building_index] == SharedLibrary.PlaceableBuildingType.ScrapDepot) {
                slot.placedBuilding.GetComponent<ScrapDepot>().controller = this;
            } 
            slot.placedBuilding.transform.localPosition = Vector3.zero;
            slot.placedBuilding.transform.localPosition += new Vector3(0, slot.placedBuilding.transform.localScale.y /1.5f, 0);
            slot.placedBuilding.transform.localRotation = Quaternion.identity;
            Debug.Log($"Built {slot.placedBuilding.name} at slot {slot.index}"); 
        }
        
    }
    // Callback for when the elevator is empty of scrap
    public void OnElevatorEmpty() { StartCoroutine(elevator.Regen()); if (scrap_depot_tutorial) {starting_scrap_depot.tutorial_text.enabled = true; scrap_depot_tutorial = false;}}
    // Callback for when the player places scrap into a depot 
    public void OnDepotSelect(SelectEnterEventArgs args) {
        GameObject selected = args.interactableObject.transform.gameObject;
        scrap_depot_tutorial = false;
        starting_scrap_depot.tutorial_text.enabled = false;
        if (selected == null) { return; }
        ScrapDepot depot = selected.transform.parent.GetComponent<ScrapDepot>();
        
        if (depot == null) { return; }
        if (carried_regular_scrap == 0 && carried_electronic_scrap == 0) { return; }
        depot.regular_scrap_count += carried_regular_scrap;
        depot.electronic_scrap_count += carried_electronic_scrap;
        depot.UpdateStatusText();

        carried_regular_scrap = 0;
        carried_electronic_scrap = 0;
        UpdateInvText();
        Debug.Log($"ScrapDepot: regular scrap = {depot.regular_scrap_count}, electronic scrap = {depot.electronic_scrap_count}");
        if (build_mode_tutorial) {
            build_mode_tutorial_text.enabled = true;
            build_mode_tutorial = false;
        }
    }
    // Callback for when the player selects scrap from the elevator or from pile
    public void OnScrapSelect(SelectEnterEventArgs args) {
        GameObject selected = args.interactableObject.transform.gameObject;
        Debug.Log(selected == null);
        if (selected.GetComponent<ElectronicScrap>() != null) {
            if (carried_electronic_scrap + carried_regular_scrap == carry_capacity) {
                Debug.Log("Can't pick up electronic scrap, at capacity");
            } else {
                carried_electronic_scrap++;
                elevator.electronic_scrap--;
                UpdateInvText();
                Debug.Log($"Carried electronic scrap: {carried_electronic_scrap}");
                Destroy(selected);
                if (elevator.electronic_scrap == 0 && elevator.regular_scrap == 0) { OnElevatorEmpty(); }
            }
            
        } else if (selected.GetComponent<RegularScrap>() != null) {
            if (carried_electronic_scrap + carried_regular_scrap == carry_capacity) {
                Debug.Log("Can't pick up regular scrap, at capacity");
            } else {
                carried_regular_scrap++;
                elevator.regular_scrap--;
                UpdateInvText();
                Debug.Log($"Carried regular scrap: {carried_regular_scrap}");
                Destroy(selected);
                if (elevator.electronic_scrap == 0 && elevator.regular_scrap == 0) { OnElevatorEmpty(); }
            }
            
        }
    }
    void Start() {
        carried_regular_scrap = 0;
        carried_electronic_scrap = 0;
        current_building_index = 0;
        scrap_depot_tutorial = true;
        build_mode_tutorial = true;
        build_mode = false;
        string b = "";
        switch (building_types[current_building_index]) {
            case SharedLibrary.PlaceableBuildingType.ScrapDepot: { b = "Scrap Depot"; break; }
            case SharedLibrary.PlaceableBuildingType.Fabricator: { b = "Fabricator"; break; }
            case SharedLibrary.PlaceableBuildingType.LaunchBay: { b = "Launch Bay"; break; }
        }
        bstr = b;
        UpdateInvText();
        UpdateBuildText();
        build_mode_action.action.Enable();
        build_mode_action.action.performed += (ctx) => {
            if (build_mode) {
                OnBuildModeExit();
            } else {
                OnBuildModeEnter();
            }
        };
        cycle_building_selection_action.action.Enable();
        cycle_building_selection_action.action.performed += (ctx) => {
            OnBuildingSelectionCycle();
        };
    }

    // Update is called once per frame
    void Update() {
        
    }
}
