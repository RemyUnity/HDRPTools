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

    static HDRenderPipeline _hdrp;
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

    static float[] _exposureArray = new float[1];
    static ComputeBuffer s_exposureReadbackBuffer;
    static ComputeBuffer bs_exposureReadbackBuffer
    {
        get
        {
            if (s_exposureReadbackBuffer == null)
                s_exposureReadbackBuffer = new ComputeBuffer(1, sizeof(float));
            return s_exposureReadbackBuffer;
        }
    }

    static float GPUReadbackExposureFromTexture( RTHandle exposureTexture )
    {
        s_exposureReadbackCompute.SetBuffer(0, "ExposureOutput", bs_exposureReadbackBuffer);
        s_exposureReadbackCompute.SetTexture(0, "ExposureTexture", exposureTexture.rt);

        s_exposureReadbackCompute.Dispatch(0, 1, 1, 1);

        s_exposureReadbackBuffer.GetData(_exposureArray);

        return _exposureArray[0];
    }

    class GetExposureCameraData
    {
        float _exposure;
        public float exposure => GetExposure();
        HDCamera _hdCamera;
        int _lastFrameRefreshed;
        RTHandle _exposureTexture;

        public GetExposureCameraData(Camera camera, HDRenderPipeline hdrp, bool enabled = true)
        {
            _exposure = 0;
            _hdCamera = HDCamera.GetOrCreate(camera);
            _lastFrameRefreshed = 0;
            RefreshExposureTexture(hdrp);
        }

        public void RefreshExposureTexture(HDRenderPipeline hdrp)
        {
            _exposureTexture = (RTHandle) s_mi_GetExposureTexture.Invoke(hdrp, new object[] { _hdCamera });
        }

        float GetExposure()
        {
            if (_lastFrameRefreshed != Time.renderedFrameCount)
            {
                _lastFrameRefreshed = Time.renderedFrameCount;
                _exposure = GPUReadbackExposureFromTexture(_exposureTexture);
            }

            return _exposure;
        }
    }

    private Dictionary<Camera, GetExposureCameraData> _cameraExposureData = new Dictionary<Camera, GetExposureCameraData>();

    private void OnEnable()
    {
        RenderPipelineManager.activeRenderPipelineCreated += RPCreated;
    }

    private void OnDisable()
    {
        RenderPipelineManager.activeRenderPipelineCreated -= RPCreated;
    }

    void RPCreated()
    {
        _hdrp = _hdrp = (HDRenderPipeline)RenderPipelineManager.currentPipeline;
    }

    private void UpdateAllRTHandles()
    {
        if (_hdrp == null)
            return;

        foreach(var kvp in _cameraExposureData)
            kvp.Value.RefreshExposureTexture(_hdrp);
    }

    public static float GetCameraExposure( Camera camera )
    {
        if (_hdrp == null)
        {
            _hdrp = _hdrp = (HDRenderPipeline)RenderPipelineManager.currentPipeline;
            s_instance.UpdateAllRTHandles();
        }

        if (_hdrp == null )
            return -42;

        if (!s_instance._cameraExposureData.ContainsKey(camera))
            s_instance._cameraExposureData.Add(camera, new GetExposureCameraData(camera, _hdrp));

        return s_instance._cameraExposureData[camera].exposure;
    }
}
