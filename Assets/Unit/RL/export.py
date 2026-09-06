import torch
from stable_baselines3 import PPO

model = PPO.load("action_select_final_model")

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
wrapper.policy.to('cpu')
wrapper.eval()

dummy_input = torch.zeros((1, 8), dtype=torch.float32)  # dx, dy, delta, selfHp, targetHp, InAttackRange , distToEdge, AttackTendency

torch.onnx.export(
    wrapper, (dummy_input,), "ActionSelect_Policy.onnx",
    input_names=["observation"], output_names=["action_logits"],
    dynamic_axes={"observation": {0: "batch"}, "action_logits": {0: "batch"}},
    export_params=True, opset_version=13,
)

print("ActionSelect_Policy.onnx 파일 생성 완료!")