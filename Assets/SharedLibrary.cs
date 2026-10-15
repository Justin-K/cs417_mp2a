using UnityEngine;
using System;
using System.Collections;
public class SharedLibrary {
	public enum ScrapType {
		Regular,
		Electronic
	}
	
	public enum PlaceableBuildingType {
        ScrapDepot,
        Fabricator,
        LaunchBay,
    }
    
	public static IEnumerator AnimatePositionChange(GameObject obj, Vector3 src, Vector3 dst, float duration, Action callback) {
		float d = Mathf.Max(duration, 0.001f);
        float elapsed = 0f;

        while (elapsed < d) {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / d);

            t = t * t * (3f - 2f * t);
            obj.transform.position = Vector3.Lerp(src, dst, t);
            yield return null;
        }

        obj.transform.position = dst;
        callback();
	}
	public static IEnumerator AnimateRotationChange(GameObject obj, Vector3 new_angle, float duration, Action callback) {
        float d = Mathf.Max(duration, 0.001f);
        Quaternion start_rotation = obj.transform.localRotation;
        Quaternion end_rotation = start_rotation * Quaternion.Euler(new_angle);
        float elapsed = 0f;
        while (elapsed < d) {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / d);

            t = t * t * (3f - 2f * t);

            obj.transform.localRotation = Quaternion.Lerp(start_rotation, end_rotation, t);
            yield return null;
        }
        obj.transform.localRotation = end_rotation;
        callback();
	}
};
	
