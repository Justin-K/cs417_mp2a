using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
public class Elevator : MonoBehaviour {
    private bool car_is_up;
    private bool doors_closed;
    private bool transition_in_progress;
    private readonly List<Rigidbody> scrap_on_car = new List<Rigidbody>();
    public GameController controller;
    public GameObject[] scrap_object_pool;
    public GameObject spawn_ref;
    public GameObject elevator_car;
    public GameObject elevator_car_up_ref;
    public GameObject elevator_car_down_ref;
    public GameObject left_door;
    public GameObject right_door;
    public GameObject left_door_open_ref;
    public GameObject right_door_open_ref;
    public GameObject left_door_closed_ref;
    public GameObject right_door_closed_ref;
    public int delivery_timer_min_seconds;
    public int delivery_timer_max_seconds;
    public TextMeshPro tutorial_text;
    private bool show_tutorial = true;
    public int electronic_scrap;
    public int regular_scrap;
    
    public float car_travel_seconds = 3f;
    public float door_travel_seconds = 2f;

    public int scrap_per_delivery = 6;
    public float scrap_spawn_interval = 0.3f;
    public float scrap_spawn_radius = 0.35f;
    public float scrap_settle_seconds = 1.5f;

    private IEnumerator ScrapSpawn(int count) {
        if (count <= 0 || spawn_ref == null || elevator_car == null ||
            scrap_object_pool == null || scrap_object_pool.Length == 0) {
            yield break;
        }

        for (int i = 0; i < count; i++) {
            GameObject prefab = scrap_object_pool[Random.Range(0, scrap_object_pool.Length)];
            if (prefab == null) {
                continue;
            } else if (prefab.GetComponent<ElectronicScrap>() != null) {
                electronic_scrap++;
            } else if (prefab.GetComponent<RegularScrap>() != null) {
                regular_scrap++;
            }
            Vector2 offset = Random.insideUnitCircle * scrap_spawn_radius;
            Vector3 spawn_position = spawn_ref.transform.position + spawn_ref.transform.right * offset.x + spawn_ref.transform.forward * offset.y;

            Quaternion spawn_rotation = Random.rotation;
            GameObject spawned = Instantiate(
                prefab, spawn_position, spawn_rotation, elevator_car.transform);
            spawned.GetComponent<XRSimpleInteractable>().selectEntered.AddListener(controller.OnScrapSelect);
            Rigidbody body = spawned.GetComponent<Rigidbody>();
            if (body == null) {
                body = spawned.AddComponent<Rigidbody>();
            }
            body.isKinematic = false;
            body.useGravity = true;
            body.WakeUp();
            scrap_on_car.Add(body);

            if (spawned.GetComponentInChildren<Collider>() == null) {
                Debug.LogWarning(
                    "Spawned scrap has no 3D Collider and will fall through the elevator floor.",
                    spawned);
            }
            if (scrap_spawn_interval > 0f) {
                yield return new WaitForSeconds(scrap_spawn_interval);
            } else {
                yield return null;
            }
        }
    }

    public void SpawnScrap(int count) {
        StartCoroutine(ScrapSpawn(count));
    }
    private IEnumerator AnimateDoors(bool open) {
        Vector3 left_target = (open ? left_door_open_ref : left_door_closed_ref).transform.position;
        Vector3 right_target = (open ? right_door_open_ref : right_door_closed_ref).transform.position;

        Coroutine left_animation = StartCoroutine(SharedLibrary.AnimatePositionChange(
            left_door, left_door.transform.position, left_target,
            door_travel_seconds, () => { }));

        Coroutine right_animation = StartCoroutine(SharedLibrary.AnimatePositionChange(
            right_door, right_door.transform.position, right_target,
            door_travel_seconds, () => { }));

        yield return left_animation;
        yield return right_animation;
        doors_closed = !open;
    }

    public IEnumerator OpenDoors() {
        if (!doors_closed) {
            yield break;
        }
        yield return AnimateDoors(true);
    }

    public IEnumerator CloseDoors() {
        if (doors_closed) {
            yield break;
        }
        yield return AnimateDoors(false);
    }

    private void SetCargoKinematic(bool isKinematic) {
        for (int i = scrap_on_car.Count - 1; i >= 0; i--) {
            Rigidbody body = scrap_on_car[i];
            if (body == null || !body.transform.IsChildOf(elevator_car.transform)) {
                scrap_on_car.RemoveAt(i);
                continue;
            }

            body.isKinematic = isKinematic;
            if (!isKinematic) {
                body.WakeUp();
            }
        }
    }

    private IEnumerator MoveCar(bool move_up) {
        if (car_is_up == move_up) {
            yield break;
        }

        Vector3 target = (move_up ? elevator_car_up_ref : elevator_car_down_ref).transform.position;
        SetCargoKinematic(true);

        yield return SharedLibrary.AnimatePositionChange(
            elevator_car, elevator_car.transform.position, target,
            car_travel_seconds, () => { });

        car_is_up = move_up;
        SetCargoKinematic(false);
    }

    // Call with StartCoroutine(elevator.RaiseAndOpenDoors()).
    public IEnumerator RaiseAndOpenDoors() {
        if (transition_in_progress) {
            yield break;
        }
        transition_in_progress = true;
        if (!doors_closed) {
            yield return CloseDoors();
        }
        yield return MoveCar(true);
        yield return OpenDoors();
        transition_in_progress = false;
        
    }

    public IEnumerator CloseDoorsAndLowerCar() {
        if (transition_in_progress) {
            yield break;
        }

        transition_in_progress = true;
        yield return CloseDoors();
        yield return MoveCar(false);
        transition_in_progress = false;
        //status_text.enabled = false;
    }
    public IEnumerator Regen() {
        yield return CloseDoorsAndLowerCar();
        yield return Deliver();
    }
    public IEnumerator Deliver() {
        if (car_is_up || !doors_closed || transition_in_progress) {
            yield break;
        }

        int min_delay = Mathf.Max(0, Mathf.Min(delivery_timer_min_seconds, delivery_timer_max_seconds));
        int max_delay = Mathf.Max(min_delay, Mathf.Max(delivery_timer_min_seconds, delivery_timer_max_seconds));
        int delivery_timer = Random.Range(min_delay, max_delay + 1);
        yield return new WaitForSeconds(delivery_timer);

        // Load the car while it is down, allow gravity to form a pile, then
        // lift the pile and open the doors when the car reaches the top.
        yield return ScrapSpawn(scrap_per_delivery);
        if (scrap_settle_seconds > 0f) {
            yield return new WaitForSeconds(scrap_settle_seconds);
        }
        yield return RaiseAndOpenDoors();
        if (show_tutorial) {
            tutorial_text.enabled = true;
            yield return new WaitForSeconds(5);
            show_tutorial = false;
            tutorial_text.enabled = false;
        }

        

    }

    void Start() {
        elevator_car.transform.position = elevator_car_down_ref.transform.position;
        left_door.transform.position = left_door_closed_ref.transform.position;
        right_door.transform.position = right_door_closed_ref.transform.position;
        regular_scrap = 0;
        electronic_scrap = 0;
        car_is_up = false;
        doors_closed = true;
        
        StartCoroutine(Deliver());
    }
}
