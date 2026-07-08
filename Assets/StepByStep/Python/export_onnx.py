"""
DQN으로 학습된 QNet 체크포인트(phase1_dqn_checkpoint.pt)를 ONNX로 export.
PPO용 export_onnx.py와는 모델 구조가 다르므로 별도 스크립트로 분리.

사용법: python export_dqn_onnx.py [체크포인트경로]
기본값: phase1_dqn_checkpoint.pt (dqn_train_server.py가 저장하는 기본 파일명)
"""

import sys
import torch
import torch.nn as nn

OBS_DIM = 9
ACTION_DIM = 6


class QNet(nn.Module):
    """dqn_train_server.py의 QNet과 완전히 동일한 구조여야 state_dict가 맞는다."""

    def __init__(self, obs_dim=OBS_DIM, hidden=64, action_dim=ACTION_DIM):
        super().__init__()
        self.net = nn.Sequential(
            nn.Linear(obs_dim, hidden),
            nn.ReLU(),
            nn.Linear(hidden, hidden),
            nn.ReLU(),
            nn.Linear(hidden, action_dim),
        )

    def forward(self, x):
        return self.net(x)  # Q값 6개. Unity의 OutputToAction()은 이걸 그대로 argmax.


def export(checkpoint_path: str, output_path: str = "phase1_dqn_policy.onnx"):
    model = QNet(obs_dim=OBS_DIM, action_dim=ACTION_DIM)
    model.load_state_dict(torch.load(checkpoint_path, map_location="cpu"))
    model.eval()

    dummy_input = torch.zeros((1, OBS_DIM), dtype=torch.float32)

    torch.onnx.export(
        model,
        (dummy_input,),
        output_path,
        input_names=["observation"],
        output_names=["action_logits"],  # 실제로는 Q값이지만, Unity의 argmax 로직은 동일하게 적용 가능
        dynamic_axes={
            "observation": {0: "batch"},
            "action_logits": {0: "batch"},
        },
        export_params=True,
        opset_version=13,
    )
    print(f"ONNX export 완료: {output_path}")


if __name__ == "__main__":
    ckpt = sys.argv[1] if len(sys.argv) > 1 else "phase1_dqn_checkpoint.pt"
    export(ckpt)