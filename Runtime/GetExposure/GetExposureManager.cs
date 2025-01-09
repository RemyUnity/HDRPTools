using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public class GetExposureManager : MonoBehaviour
{
    private static GetExposureManager bs_instance;
    private static GetExposureManager s_instance
    {
        get
        {
            if (bs_instance == null)
                bs_instance = FindAnyObjectByType<GetExposureManager>(FindObjectsInactive.Include);
            if (bs_instance == null)
            {
                bs_instance = new GameObject("GetExposure Manager").AddComponent<GetExposureManager>();
                bs_instance.gameObject.hideFlags = HideFlags.HideAndDontSave;
            }

            return bs_instance;
        }
    }

    class GetExposureCameraData
    {
        public bool getExposureEnabled;
        public float exposure;
        public HDCamera hdCamera;
    }

    private Dictionary<Camera, GetExposureCameraData> _cameraExposureData = new Dictionary<Camera, GetExposureCameraData>();

    HDRenderPipeline _hdrp;
    RTHandle _exposureTexture;
    static MethodInfo sb_mi_GetExposureTexture;
    static MethodInfo s_mi_GetExposureTexture
    {
        get
        {
            if (sb_mi_GetExposureTexture == null)
            {
                sb_mi_GetExposureTexture = typeof(HDRenderPipeline).GetMethod("GetExposureTexture", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            return sb_mi_GetExposureTexture;
        }
    }

    static MethodInfo sb_mi_RequestGpuExposureValue;
    static MethodInfo s_mi_RequestGpuExposureValue
    {
        get
        {
            if (sb_mi_RequestGpuExposureValue == null)
            {
                sb_mi_RequestGpuExposureValue = typeof(HDCamera).GetMethod("RequestGpuExposureValue", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            return sb_mi_RequestGpuExposureValue;
        }
    }

    static MethodInfo sb_mi_GpuExposureValue;
    static MethodInfo s_mi_GpuExposureValue
    {
        get
        {
            if (sb_mi_GpuExposureValue == null)
            {
                sb_mi_GpuExposureValue = typeof(HDCamera).GetMethod("GpuExposureValue", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            return sb_mi_GpuExposureValue;
        }
    }

    static MethodInfo sb_mi_GpuDeExposureValue;
    static MethodInfo s_mi_GpuDeExposureValue
    {
        get
        {
            if (sb_mi_GpuDeExposureValue == null)
            {
                sb_mi_GpuDeExposureValue = typeof(HDCamera).GetMethod("GpuDeExposureValue", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            return sb_mi_GpuDeExposureValue;
        }
    }


    static ComputeShader bs_exposureReadbackCompute;
    static ComputeShader s_exposureReadbackCompute
    {
        get
        {
            if (bs_exposureReadbackCompute == null)
                bs_exposureReadbackCompute = Resources.Load<ComputeShader>("GetExposure");

            return bs_exposureReadbackCompute;
        }
    }

    float[] _exposureArray = new float[1];
    ComputeBuffer _exposureReadbackBuffer;

    private void Awake()
    {
        _exposureReadbackBuffer = new ComputeBuffer(1, sizeof(float));
    }

    private void OnEnable()
    {
        RenderPipelineManager.endCameraRendering += OnCameraFinishedRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.endCameraRendering -= OnCameraFinishedRendering;
    }

    void OnCameraFinishedRendering(ScriptableRenderContext ctx, Camera cam)
    {
        /*
        _hdrp = (HDRenderPipeline)RenderPipelineManager.currentPipeline;

        if (_hdrp == null || s_exposureReadbackCompute == null)
            return;

        if (!_cameraExposureData.ContainsKey(cam))
            _cameraExposureData.Add(cam, new GetExposureCameraData()
            {
                getExposureEnabled = false,
                exposure = 0,
                hdCamera = HDCamera.GetOrCreate(cam)
            });

        if (!_cameraExposureData[cam].getExposureEnabled)
            return;

        _exposureTexture = (RTHandle) s_mi_GetExposureTexture.Invoke(_hdrp, new object[] { _cameraExposureData[cam].hdCamera });

        s_exposureReadbackCompute.SetBuffer(0, "ExposureOutput", _exposureReadbackBuffer);
        s_exposureReadbackCompute.SetTexture(0, "ExposureTexture", _exposureTexture.rt);

        s_exposureReadbackCompute.Dispatch(0, 1, 1, 1);

        _exposureReadbackBuffer.GetData(_exposureArray);

        _cameraExposureData[cam].exposure = _exposureArray[0];
        */
    }

    public static void SetCameraExposureReadbackActive( Camera camera, bool state )
    {
        if (s_instance._cameraExposureData.ContainsKey(camera))
            s_instance._cameraExposureData[camera].getExposureEnabled = state;
        else
            s_instance._cameraExposureData.Add(camera, new GetExposureCameraData()
            {
                getExposureEnabled = state,
                exposure = 0,
                hdCamera = HDCamera.GetOrCreate(camera)
            });
    }

    public static float GetCameraExposure( Camera camera )
    {
        if (!s_instance._cameraExposureData.ContainsKey(camera))
            s_instance._cameraExposureData.Add(camera, new GetExposureCameraData()
            {
                getExposureEnabled = true,
                exposure = 0,
                hdCamera = HDCamera.GetOrCreate(camera)
            });

        s_instance._cameraExposureData[camera].getExposureEnabled = true;

        return s_instance._cameraExposureData[camera].exposure;
    }

    private void Update()
    {
        _hdrp = (HDRenderPipeline)RenderPipelineManager.currentPipeline;
        if (_hdrp == null)
            return;

        foreach (var kvp in _cameraExposureData)
        {
            if (kvp.Value.getExposureEnabled)
            {
                var exposureTexture = (RTHandle) s_mi_GetExposureTexture.Invoke(_hdrp, new object[] { kvp.Value.hdCamera });
                s_mi_RequestGpuExposureValue.Invoke(kvp.Value.hdCamera, new object[] { exposureTexture });

                float exp = (float)s_mi_GpuExposureValue.Invoke(kvp.Value.hdCamera, null);

                exp = Mathf.Log(1.0f/exp) / Mathf.Log(2);

                kvp.Value.exposure = exp;
            }
        }
    }
}
