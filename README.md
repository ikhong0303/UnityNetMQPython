# UnityNetMQPython
문서
https://www.notion.so/Unity-NetMQ-Python-2670017842be801db1c9ef56f44434d2?showMoveTo=true&saveParent=true
UnityNetMQPython
파이썬 코드 아래 첨부
import zmq
import numpy as np
import json
import base64
import wave
import io

context = zmq.Context()
socket = context.socket(zmq.REP)
socket.bind("tcp://*:5555")

print("AI 에코 서버 v2 (채널 지원)가 5555 포트에서 대기 중...")

# --- WAV 헤더를 생성하기 위한 정보 ---
SAMPLE_WIDTH = 2  # 16-bit 오디오 (2 bytes)
FRAME_RATE = 44100  # Unity에서 녹음한 샘플 레이트와 동일하게 설정

while True:
    # 1. 유니티로부터 JSON 데이터 수신
    request_json = socket.recv_string()
    request_data = json.loads(request_json)

    channels = request_data["channels"]
    audio_b64 = request_data["audio_b64"]
    
    # Base64 디코딩하여 원본 오디오 바이트 복원
    audio_bytes = base64.b64decode(audio_b64)
    print(f"오디오 데이터 수신 (채널: {channels}, 크기: {len(audio_bytes)} bytes)")

    # 2. 받은 바이트를 float 배열로 변환
    audio_floats = np.frombuffer(audio_bytes, dtype=np.float32)

    # ----------------------------------------------------
    # (AI 모델 처리 부분 - 현재는 에코 모드)
    print("AI 모델 처리 중...")
    processed_floats = audio_floats
    # ----------------------------------------------------

    # 3. float 배열을 WAV 파일 형식(16-bit int)으로 변환
    audio_ints = (processed_floats * 32767).astype(np.int16)

    # 4. 메모리 상에서 정확한 채널 수로 WAV 파일 생성
    buffer = io.BytesIO()
    with wave.open(buffer, 'wb') as wf:
        wf.setnchannels(channels)  # 유니티에서 받은 채널 수 사용
        wf.setsampwidth(SAMPLE_WIDTH)
        wf.setframerate(FRAME_RATE)
        wf.writeframes(audio_ints.tobytes())
    
    response_audio_bytes = buffer.getvalue()
    print("메모리에서 WAV 파일 생성 완료")

    # 5. 결과를 JSON으로 포장하여 전송
    response_data = {
        "gesture": "wave_hand",
        "audio_b64": base64.b64encode(response_audio_bytes).decode('utf-8')
    }
    json_response = json.dumps(response_data)
    socket.send_string(json_response)
    print("결과 전송 완료!")
    #python ai_server.py
