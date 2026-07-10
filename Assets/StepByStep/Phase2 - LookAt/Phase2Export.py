import torch
from stable_baselines3 import PPO

# 1. 저장된 zip 모델 로드
model = PPO.load("phase2_ppo_model")

# 2. ONNX 익스포트용 래퍼 클래스 (결정론적 액션 평균값 추출)
class PolicyWrapper(torch.nn.Module):
    def __init__(self, sb3_model):
        super().__init__()
        self.policy = sb3_model.policy

    def forward(self, obs):
        features = self.policy.extract_features(obs)
        latent_pi, _ = self.policy.mlp_extractor(features)
        mean_actions = self.policy.action_net(latent_pi)
        return mean_actions

wrapper = PolicyWrapper(model)
wrapper.eval()

# 3. 유니티 센티스 입력 형태 정의 (배치 1, 관측값 2개)
dummy_input = torch.zeros((1, 2), dtype=torch.float32)

# 4. ONNX 변환 및 저장
torch.onnx.export(
    wrapper, (dummy_input,), "Phase2_Policy.onnx",
    input_names=["observation"], output_names=["action"],
    dynamic_axes={"observation": {0: "batch"}, "action": {0: "batch"}},
    export_params=True, opset_version=13,
)

print("Phase2_Policy.onnx 파일 생성 완료!")