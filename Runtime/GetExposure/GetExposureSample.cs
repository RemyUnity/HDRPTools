using UnityEngine;

public class GetExposureSample : MonoBehaviour
{
    [SerializeField]
    private float _mainCameraExposure = 0;

    private Camera _mainCamera;

    private void Start()
    {
        _mainCamera = Camera.main;
    }

    private void Update()
    {
        float exposure = GetExposureManager.GetCameraExposure(_mainCamera);
        _mainCameraExposure = exposure;
    }
}
