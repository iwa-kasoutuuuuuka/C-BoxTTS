import os
import sys

# インポートパスの調整
sys.path.insert(0, os.path.abspath(os.path.dirname(__file__)))

from src.utils import split_text, merge_audio
import torch

def test_split_text():
    text = "こんにちは！元気ですか？。今日は、良い天気ですね。Bye bye!"
    segments = split_text(text)
    print(f"Original: {text}")
    print("Segments:")
    for s in segments:
        print(f" - '{s}'")
    
    assert len(segments) > 0
    assert "こんにちは！" in segments
    assert "元気ですか？。" in segments
    # 。と、の扱いを確認
    assert "今日は、良い天気ですね。" in segments or "今日は、良い天気ですね" in segments

def test_merge_audio():
    tensors = [torch.randn(1, 1000), torch.randn(1, 1000)]
    merged = merge_audio(tensors, 1000, silence_sec=0.1)
    # 1000 + 100(silence) + 1000 = 2100
    print(f"Merged shape: {merged.shape}")
    assert merged.shape[1] == 2100

if __name__ == "__main__":
    try:
        test_split_text()
        test_merge_audio()
        print("\nLogic Test Passed!")
    except Exception as e:
        print(f"\nLogic Test Failed: {e}")
        import traceback
        traceback.print_exc()
