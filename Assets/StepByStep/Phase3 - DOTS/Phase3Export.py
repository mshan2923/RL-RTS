import torch
import torch.nn as nn
from stable_baselines3 import PPO
from stable_baselines3.common.torch_layers import MlpExtractor

# 정책 네트워크를 ONNX로 수출하기 위한 래퍼 클래스
class ActorWrapper(nn.Module):
    def __init__(self, policy):
        super().__init__()
        self.policy = policy

    def forward(self, obs):
        # SB3의 policy는 predict 시 다양한 로직이 섞여 있으니
        # 가장 기초적인 '추론'만 담당하는 MLP 통과 로직을 타야 해
        # features_extractor(obs) -> mlp_extractor -> action_net
        features = self.policy.features_extractor(obs)
        latent_pi, _ = self.policy.mlp_extractor(features)
        action_logits = self.policy.action_net(latent_pi)
        return action_logits

def export_ppo_to_onnx(model_path, output_path):
    model = PPO.load(model_path)
    
    # 래퍼 적용
    actor_net = ActorWrapper(model.policy)
    actor_net.eval()
    
    dummy_input = torch.randn(1, 2, dtype=torch.float32)
    
    torch.onnx.export(
        actor_net,
        dummy_input,
        output_path,
        export_params=True,
        opset_version=14,
        input_names=['input_obs'],
        output_names=['output_action'],
        dynamic_axes={'input_obs': {0: 'batch_size'}, 'output_action': {0: 'batch_size'}}
    )
    
    print(f"[Export] 변환 성공: {output_path}")
    print(f"[Export] 입력 차원: {dummy_input.shape}")

if __name__ == "__main__":
    # 저장했던 모델 이름에 맞춰서 경로를 넣어줘
    MODEL_FILE = "phase3_ppo_model.zip"
    ONNX_FILE = "AgentActor.onnx"
    
    export_ppo_to_onnx(MODEL_FILE, ONNX_FILE)