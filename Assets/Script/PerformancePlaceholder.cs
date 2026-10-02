using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// STEP-3 开演占位演出：灯光闪烁 + 一声锣（占位 AudioClip）+ 相机轻推。
/// STEP-8 换成 Timeline 正式演出，本脚本届时整体删除。
/// </summary>
public class PerformancePlaceholder : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("闪烁的灯（取暖灯的 Light2D）")]
    public Light2D flickerLight;
    [Tooltip("占位锣声（构建器生成的 SFX_Gong_Placeholder）")]
    public AudioClip gong;

    [Header("演出参数")]
    public float duration = 2.2f;
    [Tooltip("闪烁最低亮度比例")]
    [Range(0f, 1f)] public float flickerMin = 0.55f;
    [Tooltip("相机推近量（正交 Size 减小值，世界单位）")]
    public float camPush = 0.45f;
    [Tooltip("推近用时 / 保持 / 退回用时（秒）")]
    public float pushInTime = 0.9f;
    public float holdTime = 0.5f;
    public float pushOutTime = 0.8f;

    public bool IsPlaying => playing;

    private AudioSource src;
    private Camera cam;
    private bool playing;
    private float lightBaseIntensity = -1f;
    private float camBaseSize = -1f;

    private void Awake()
    {
        src = GetComponent<AudioSource>();
        if (src == null) src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f;
    }

    public void Play()
    {
        if (playing) return;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        playing = true;
        if (cam == null) cam = Camera.main;
        if (cam != null) camBaseSize = cam.orthographicSize;
        if (flickerLight != null) lightBaseIntensity = flickerLight.intensity;
        if (src != null && gong != null) src.PlayOneShot(gong);

        float t = 0f;
        while (t < pushInTime) // 推近 + 闪烁
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / pushInTime);
            SetCamSize(camBaseSize - camPush * EaseOutQuad(k));
            Flicker();
            yield return null;
        }
        t = 0f;
        while (t < holdTime) // 保持
        {
            t += Time.deltaTime;
            Flicker();
            yield return null;
        }
        t = 0f;
        float pushedSize = camBaseSize - camPush;
        while (t < pushOutTime) // 退回
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / pushOutTime);
            SetCamSize(Mathf.Lerp(pushedSize, camBaseSize, k));
            if (flickerLight != null) flickerLight.intensity = Mathf.Lerp(lightBaseIntensity * flickerMin, lightBaseIntensity, k);
            yield return null;
        }
        SetCamSize(camBaseSize);
        if (flickerLight != null) flickerLight.intensity = lightBaseIntensity;
        playing = false;
    }

    private void Flicker()
    {
        if (flickerLight == null) return;
        float n = Mathf.PerlinNoise(Time.time * 11f, 3.7f); // 平滑噪声抖动
        flickerLight.intensity = lightBaseIntensity * Mathf.Lerp(1f, flickerMin, n * n);
    }

    private void SetCamSize(float size)
    {
        if (cam != null) cam.orthographicSize = size;
    }

    private static float EaseOutQuad(float k) => 1f - (1f - k) * (1f - k);
}
