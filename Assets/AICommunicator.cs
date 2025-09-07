using NetMQ;
using NetMQ.Sockets;
using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

// JSON 직렬화를 위한 클래스들
[Serializable]
public class AudioPacket // 보낼 데이터
{
    public int channels;
    public string audio_b64;
}

[Serializable]
public class AIResponse // 받을 데이터
{
    public string gesture;
    public string audio_b64;
}

public class AICommunicator : MonoBehaviour
{
    [Header("연결 설정")]
    public string serverAddress = "tcp://localhost:5555";

    [Header("오디오 설정")]
    public int recordDuration = 5;
    private const int SampleRate = 44100;

    [Header("연결 대상")]
    public Animator avatarAnimator;
    public AudioSource audioSource;
    public Button recordButton;

    private Thread _networkThread;
    private bool _isRunning;
    private readonly ConcurrentQueue<string> _sendQueue = new ConcurrentQueue<string>(); // JSON 문자열을 담을 큐

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
        _networkThread?.Join(1000); // 1초간 대기

        // if문을 제거하고 Cleanup을 직접 호출
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
                        Debug.LogError("서버 응답 시간 초과!");
                    }
                }
                Thread.Sleep(50);
            }
        }
    }

    private void ProcessResponse(string json)
    {
        if (string.IsNullOrEmpty(json)) return;

        Debug.Log("서버로부터 응답 받음: " + json.Substring(0, Math.Min(json.Length, 100)));
        AIResponse response = JsonUtility.FromJson<AIResponse>(json);

        if (avatarAnimator != null && !string.IsNullOrEmpty(response.gesture))
        {
            avatarAnimator.SetTrigger(response.gesture);
            Debug.Log($"제스처 실행: {response.gesture}");
        }

        if (audioSource != null && !string.IsNullOrEmpty(response.audio_b64))
        {
            byte[] responseAudioBytes = Convert.FromBase64String(response.audio_b64);
            AudioClip clip = WavUtility.ToAudioClip(responseAudioBytes);
            if (clip != null)
            {
                audioSource.PlayOneShot(clip);
                Debug.Log("응답 음성 재생!");
            }
        }
    }

    public void StartRecording()
    {
        Debug.Log("녹음을 시작합니다...");
        var audioClip = Microphone.Start(null, false, recordDuration, SampleRate);
        StartCoroutine(WaitForRecordingToEnd(audioClip));
        if (recordButton != null) recordButton.interactable = false;
    }

    private System.Collections.IEnumerator WaitForRecordingToEnd(AudioClip clip)
    {
        yield return new WaitForSeconds(recordDuration);

        // --- 디버깅용: 녹음된 소리를 바로 재생해서 확인 ---
        Debug.Log("디버깅: 녹음된 클립을 바로 재생합니다.");
        audioSource.PlayOneShot(clip);
        // -----------------------------------------

        // 녹음된 오디오 데이터를 float[] 배열로 가져옴
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        // float[]를 byte[]로 변환
        byte[] audioBytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, audioBytes, 0, audioBytes.Length);

        // 오디오 데이터와 채널 수를 함께 패키징
        AudioPacket packet = new AudioPacket
        {
            channels = clip.channels,
            audio_b64 = Convert.ToBase64String(audioBytes)
        };

        // JSON 문자열로 변환하여 큐에 추가
        string jsonRequest = JsonUtility.ToJson(packet);
        _sendQueue.Enqueue(jsonRequest);

        Debug.Log($"녹음 완료. (채널: {clip.channels}) 데이터를 전송 큐에 추가했습니다.");
        if (recordButton != null) recordButton.interactable = true;
    }
}