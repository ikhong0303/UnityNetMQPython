using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

// JSON ����ȭ�� ���� Ŭ������
[Serializable]
public class AudioPacket // ���� ������
{
    public int channels;
    public string audio_b64;
}

[Serializable]
public class AIResponse // ���� ������
{
    public string gesture;
    public string audio_b64;
}

public class AICommunicator : MonoBehaviour
{
    [Header("���� ����")]
    public string serverAddress = "tcp://localhost:5555";

    [Header("����� ����")]
    public int recordDuration = 5;
    private const int SampleRate = 44100;

    [Header("���� ���")]
    public Animator avatarAnimator;
    public AudioSource audioSource;
    public Button recordButton;

    private Thread _networkThread;
    private bool _isRunning;
    private readonly ConcurrentQueue<string> _sendQueue = new ConcurrentQueue<string>(); // JSON ���ڿ��� ���� ť

    void Start()
    {
        if (recordButton != null)
        {
            recordButton.onClick.AddListener(StartRecording);
        }
        _isRunning = true;
        _networkThread = new Thread(NetworkLoop);
        _networkThread.Start();
    }

    void OnDestroy()
    {
        _isRunning = false;
        _networkThread?.Join(1000); // 1�ʰ� ���

        // if���� �����ϰ� Cleanup�� ���� ȣ��
        NetMQConfig.Cleanup(false);
    }

    private void NetworkLoop()
    {
        AsyncIO.ForceDotNet.Force();
        using (var client = new RequestSocket())
        {
            client.Connect(serverAddress);
            while (_isRunning)
            {
                if (_sendQueue.TryDequeue(out var jsonRequest))
                {
                    client.TrySendFrame(jsonRequest);

                    if (client.TryReceiveFrameString(TimeSpan.FromSeconds(20), out var jsonResponse))
                    {
                        UnityMainThreadDispatcher.Instance().Enqueue(() => ProcessResponse(jsonResponse));
                    }
                    else
                    {
                        Debug.LogError("���� ���� �ð� �ʰ�!");
                    }
                }
                Thread.Sleep(50);
            }
        }
    }

    private void ProcessResponse(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        Debug.Log("�����κ��� ���� ����: " + json.Substring(0, Math.Min(json.Length, 100)));
        AIResponse response = JsonUtility.FromJson<AIResponse>(json);

        if (avatarAnimator != null && !string.IsNullOrEmpty(response.gesture))
        {
            avatarAnimator.SetTrigger(response.gesture);
            Debug.Log($"����ó ����: {response.gesture}");
        }

        if (audioSource != null && !string.IsNullOrEmpty(response.audio_b64))
        {
            byte[] responseAudioBytes = Convert.FromBase64String(response.audio_b64);
            AudioClip clip = WavUtility.ToAudioClip(responseAudioBytes);
            if (clip != null)
            {
                audioSource.PlayOneShot(clip);
                Debug.Log("���� ���� ���!");
            }
        }
    }

    public void StartRecording()
    {
        Debug.Log("������ �����մϴ�...");
        var audioClip = Microphone.Start(null, false, recordDuration, SampleRate);
        StartCoroutine(WaitForRecordingToEnd(audioClip));
        if (recordButton != null) recordButton.interactable = false;
    }

    private System.Collections.IEnumerator WaitForRecordingToEnd(AudioClip clip)
    {
        yield return new WaitForSeconds(recordDuration);

        // --- ������: ������ �Ҹ��� �ٷ� ����ؼ� Ȯ�� ---
        Debug.Log("�����: ������ Ŭ���� �ٷ� ����մϴ�.");
        audioSource.PlayOneShot(clip);
        // -----------------------------------------

        // ������ ����� �����͸� float[] �迭�� ������
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        // float[]�� byte[]�� ��ȯ
        byte[] audioBytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, audioBytes, 0, audioBytes.Length);

        // ����� �����Ϳ� ä�� ���� �Բ� ��Ű¡
        AudioPacket packet = new AudioPacket
        {
            channels = clip.channels,
            audio_b64 = Convert.ToBase64String(audioBytes)
        };

        // JSON ���ڿ��� ��ȯ�Ͽ� ť�� �߰�
        string jsonRequest = JsonUtility.ToJson(packet);
        _sendQueue.Enqueue(jsonRequest);

        Debug.Log($"���� �Ϸ�. (ä��: {clip.channels}) �����͸� ���� ť�� �߰��߽��ϴ�.");
        if (recordButton != null) recordButton.interactable = true;
    }
}