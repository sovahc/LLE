"""
LLE TTS server: Kokoro + flanger (metallic robot), playback via a persistent pw-cat stream.
The model is loaded once at startup; the loader sends only text — no WAV is passed around.
The stream is fed silence between phrases so the sink never suspends and eats the first word.

Run:  python tts_server.py
Contract:
  POST /say   {"text": "...", "voice": "af_heart", "speed": 1.0}  -> {"ok": true}  (fire-and-forget)
  GET  /voices                                                        -> {"voices": [...]}
  GET  /health                                                        -> {"ok": true, "voices": N}
"""
import asyncio
from contextlib import asynccontextmanager
import numpy as np
import torch
import torchaudio.functional as torchaudio_f
from fastapi import FastAPI
from pydantic import BaseModel
from kokoro_onnx import Kokoro

MODEL = "/home/cat/kokoro/kokoro-v1.0.onnx"
VOICES = "/home/cat/kokoro/voices-v1.0.bin"
DEFAULT_VOICE = "af_nova"
TARGET_SR = 48000   # resampling of the Kokoro output (24 kHz natively)
PORT = 8081
KEEPALIVE_FRAMES = TARGET_SR // 10

# metallic robot voice: flanger + overdrive + downsampling (as in _kokoro_loop.py)
FLANGER_RATE = 0.6
FLANGER_BASE = 0.002
FLANGER_DEPTH = 0.0015
DRIVE = 2.0
DOWNSAMPLE = 2

def robot_fx(x, fs):
    """Metallic robot voice: flanger + waveshaper + downsampling (numpy)."""
    n = np.arange(len(x))
    delay = (FLANGER_BASE + FLANGER_DEPTH * np.sin(2*np.pi*FLANGER_RATE*n/fs)) * fs
    delayed = np.interp(np.clip(n - delay, 0, len(x)-1), n, x)
    y = x + 0.7 * delayed
    d2 = (FLANGER_BASE*0.6 + FLANGER_DEPTH*0.8*np.sin(2*np.pi*FLANGER_RATE*1.7*n/fs + 1.3)) * fs
    y = y + 0.5 * np.interp(np.clip(n - d2, 0, len(x)-1), n, x)
    y = np.tanh(DRIVE*y) / np.tanh(DRIVE)
    if DOWNSAMPLE > 1:
        y = np.repeat(y[::DOWNSAMPLE], DOWNSAMPLE)[:len(y)]
    peak = np.max(np.abs(y)) or 1.0
    return (y/peak*0.9).astype(np.float32)

def _generate(text, voice, speed):
    """Synchronous generation + effect. Called in a thread pool."""
    audio, sr = k.create(text, voice=voice, speed=speed)
    audio = torchaudio_f.resample(torch.from_numpy(audio), sr, TARGET_SR).numpy()
    return robot_fx(audio, TARGET_SR)

k = Kokoro(MODEL, VOICES)          # loaded once at process start
_lock = asyncio.Lock()             # keeps generation in request order
_tasks = set()                     # holds fire-and-forget tasks: the loop keeps only a weak ref
_pending = asyncio.Queue()
_player = None

async def _pump():
    """Sole writer to the player: pending phrases, silence when there are none."""
    silence = np.zeros(KEEPALIVE_FRAMES, np.float32).tobytes()
    while True:
        try:
            chunk = _pending.get_nowait()
        except asyncio.QueueEmpty:
            chunk = silence
        _player.stdin.write(chunk)
        await _player.stdin.drain()

@asynccontextmanager
async def lifespan(app):
    global _player
    _player = await asyncio.create_subprocess_exec(
        "pw-cat", "--playback", "--raw", "--format", "f32",
        "--rate", str(TARGET_SR), "--channels", "1", "-",
        stdin=asyncio.subprocess.PIPE)
    pump = asyncio.create_task(_pump())
    yield
    pump.cancel()
    _player.terminate()
    await _player.wait()

app = FastAPI(title="LLE TTS", lifespan=lifespan)

class SayRequest(BaseModel):
    text: str
    voice: str = DEFAULT_VOICE
    speed: float = 1.0

async def _speak(text, voice, speed):
    async with _lock:
        audio = await asyncio.to_thread(_generate, text, voice, speed)
    await _pending.put(audio.tobytes())
    print(f"[tts] queued {len(audio)/TARGET_SR:.2f}s  voice={voice}  text={text!r}", flush=True)

@app.post("/say")
async def say(req: SayRequest):
    if req.voice not in k.voices:
        return {"ok": False, "error": f"unknown voice {req.voice}"}
    task = asyncio.create_task(_speak(req.text, req.voice, req.speed))  # do not block the response
    _tasks.add(task)
    task.add_done_callback(_tasks.discard)
    return {"ok": True, "voice": req.voice}

@app.get("/voices")
def voices():
    return {"voices": sorted(k.voices.keys())}

@app.get("/health")
def health():
    return {"ok": True, "voices": len(k.voices)}

if __name__ == "__main__":
    import uvicorn
    print(f"[tts] model ready, voices={len(k.voices)} -> http://127.0.0.1:{PORT}", flush=True)
    uvicorn.run(app, host="127.0.0.1", port=PORT, log_level="warning")
