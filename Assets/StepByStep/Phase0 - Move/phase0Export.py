import torch
import torch.nn as nn
import torch.onnx

# 메인 학습 코드와 동일한 구조여야 함
class TinyQNet(nn.Module):
    def __init__(self, obs_dim=2, hidden=32, action_dim=6):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(obs_dim, hidden),
            nn.LeakyReLU(0.01),
            nn.Linear(hidden, hidden),
            nn.LeakyReLU(0.01),
            nn.Linear(hidden, action_dim),
        )

    def forward(self, x):
        return self.net(x)

def export_to_onnx(weights_path="model.pth", onnx_path="tiny_qnet.onnx"):
    # 모델 인스턴스 생성
    model = TinyQNet()
    
    # 가중치 로드
    try:
        model.load_state_dict(torch.load(weights_path, map_location=torch.device('cpu')))
        print(f"가중치 로드 완료: {weights_path}")
    except Exception as e:
        print(f"가중치 로드 실패: {e}")
        return

    model.eval()
    
    # 더미 입력 (1, 2)
    dummy_input = torch.randn(1, 2)
    
    # ONNX 변환
    torch.onnx.export(
        model,
        dummy_input,
        onnx_path,
        export_params=True,
        opset_version=12,
        do_constant_folding=True,
        input_names=['obs'],
        output_names=['q_values'],
        dynamic_axes={'obs': {0: 'batch_size'}, 'q_values': {0: 'batch_size'}}
    )
    print(f"ONNX 추출 성공: {onnx_path}")

if __name__ == "__main__":
    export_to_onnx()