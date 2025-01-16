using System;
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

    /// <summary>
    /// Helper class to handle individual camera datas to retrieve the exposure.
    /// </summary>
    internal class GetExposureCameraData
    {
        float _exposure;
        float _deExposure;
        HDCamera _hdCamera;
        int _lastFrameRefreshed;
        RTHandle _exposureTexture;

        /// <summary>
        /// Defines if the async readback is called again after it finishes.
        /// </summary>
        internal bool _autoRefresh = true;
        /// <summary>
        /// Defines if the async readback should be called each frame by the manager.
        /// </summary>
        internal bool _everyFrameRefresh = false;

        AsyncGPUReadbackRequest _lastAsyncGPUReadbackRequest;

        List<Action<float>> _asyncReadbackActions = new List<Action<float>>();

        /// <summary>
        /// Create a new GetExposureCameraData object.
        /// </summary>
        /// <param name="camera">Target camera.</param>
        /// <param name="hdrp">Current HDRenderPipeline to get the exposure RTHandle.</param>
        /// <param name="autoRefresh">Async auto refresh. Default is true.</param>
        /// <param name="everyFrameRefresh">Async every frame refresh. Default is false.</param>
        internal GetExposureCameraData(Camera camera, HDRenderPipeline hdrp, bool autoRefresh = true, bool everyFrameRefresh = false)
        {
            _exposure = 0;
            _hdCamera = HDCamera.GetOrCreate(camera);
            _lastFrameRefreshed = 0;
            RefreshExposureTexture(hdrp);
            _autoRefresh = autoRefresh;
            _everyFrameRefresh = everyFrameRefresh;
        }

        /// <summary>
        /// Refresh the exposure RTHandle from a new HDRenderPipeline.
        /// </summary>
        /// <param name="hdrp">HDRenderPipeline object to fetch the RTHandle from.</param>
        internal void RefreshExposureTexture(HDRenderPipeline hdrp)
        {
            _exposureTexture = (RTHandle) s_mi_GetExposureTexture.Invoke(hdrp, new object[] { _hdCamera });
        }

        /// <summary>
        /// Get the camera exposure in an async manner. Returns the cached exposure value from last readback request, and starts a new request if necessary.
        /// And optional readback action can be provided that is called when the request is done.
        /// </summary>
        /// <param name="asyncReadbackAction">Optional readback action. The exposure is passed as argument.</param>
        /// <returns></returns>
        internal float GetExposureAsync(Action<float> asyncReadbackAction = null)
        {
            if (_lastFrameRefreshed != Time.renderedFrameCount)
            {
                _lastFrameRefreshed = Time.renderedFrameCount;

                if (asyncReadbackAction != null)
                    _asyncReadbackActions.Add(asyncReadbackAction);

                if (_lastAsyncGPUReadbackRequest.done)
                    UpdateExposureRequest();
            }

            return _exposure;
        }

        /// <summary>
        /// Get the exposure value instantly. Forces to uses a compute shader readback.
        /// </summary>
        /// <returns></returns>
        internal float GetExposureNow()
        {
            _exposure = GPUReadbackExposureFromTexture(_exposureTexture);
            return _exposure;
        }

        // Callback called at the end of an exposure readback request, updates the cached exposure value and call the waiting readback actions.
        void SetExposureFromRequestCallback( AsyncGPUReadbackRequest request )
        {
            if (request.done && !request.hasError)
            {
                _lastFrameRefreshed = Time.renderedFrameCount;
                var value = request.GetData<Vector2>();
                _deExposure = value[0].x;
                _exposure = value[0].y;

                foreach (var action in _asyncReadbackActions)
                    action(_exposure);

                _asyncReadbackActions.Clear();

                if (_autoRefresh & !_everyFrameRefresh)
                    UpdateExposureRequest();
            }
        }

        /// <summary>
        /// Update the current exposure request by a new one. Used to restart a request after it was done, or force a new one every frame.
        /// </summary>
        internal void UpdateExposureRequest()
        {
            _lastAsyncGPUReadbackRequest = AsyncGPUReadback.Request(_exposureTexture.rt, 0, 0, 1, 0, 1, 0, 1, _everyFrameRefresh? null : SetExposureFromRequestCallback);
        }
    }

    private Dictionary<Camera, GetExposureCameraData> _cameraExposureData = new Dictionary<Camera, GetExposureCameraData>();
    private GetExposureCameraData GetOrCreateData(Camera camera)
    {
        if (!s_instance._cameraExposureData.ContainsKey(camera))
            s_instance._cameraExposureData.Add(camera, new GetExposureCameraData(camera, _hdrp));

        return _cameraExposureData[camera];
    }

    /// <summary>
    /// Updates all registered cameras RT handles, in case the HDRP asset has changed.
    /// </summary>
    private void UpdateAllRTHandles()
    {
        if (_hdrp == null)
            return;

        foreach (var kvp in _cameraExposureData)
            kvp.Value.RefreshExposureTexture(_hdrp);
    }

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
        _hdrp = (HDRenderPipeline)RenderPipelineManager.currentPipeline;
        UpdateAllRTHandles();
    }

    /// <summary>
    /// Get the current exposure value of a camera (in EV100).
    /// </summary>
    /// <param name="camera">Target camera.</param>
    /// <param name="async">Use async readback to get the exposure. Default is true.</param>
    /// <param name="asyncReadbackAction">In case of async readback, this action is called once the exposure had been updated, and send as parameter.</param>
    /// <returns></returns>
    public static float GetCameraExposure( Camera camera, bool async = true, Action<float> asyncReadbackAction = null )
    {
        if (_hdrp == null)
        {
            _hdrp = _hdrp = (HDRenderPipeline)RenderPipelineManager.currentPipeline;
            s_instance.UpdateAllRTHandles();
        }

        if (_hdrp == null )
            return -42;
        
        var cameraData = s_instance.GetOrCreateData(camera);

        if (async)
            return cameraData.GetExposureAsync();
        else
            return cameraData.GetExposureNow();
    }

    /// <summary>
    /// Enables or disables calling a new async readback when the previous one finishes, to update the cached exposure value.
    /// </summary>
    /// <param name="camera">Target camera.</param>
    /// <param name="state">Auto refresh state.</param>
    public static void SetAsyncAutoRefreshForCameraExposure( Camera camera, bool state )
    {
        s_instance.GetOrCreateData(camera)._autoRefresh = state;
    }

    /// <summary>
    /// Enables or disables the async readback every frame, to allow for more frequent updates, at the cost of more performances.
    /// </summary>
    /// <param name="camera">Target camera.</param>
    /// <param name="state">Every Frame Refresh state.</param>
    public static void SetAsyncEveryFrameRefreshForCameraExposure(Camera camera, bool state )
    {
        s_instance.GetOrCreateData(camera)._everyFrameRefresh = state;
    }

    private void Update()
    {
        foreach(var kvp in _cameraExposureData)
        {
            if (kvp.Value._everyFrameRefresh)
                kvp.Value.UpdateExposureRequest();
        }
    }
}
