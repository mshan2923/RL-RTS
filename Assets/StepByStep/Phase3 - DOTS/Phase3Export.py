import torch
from stable_baselines3 import PPO

# 1. 저장된 zip 모델 로드
model = PPO.load("phase3_ppo_model")

# 저장된 모델에서 에이전트 수(num_agents) 자동 추출
# 예: observation_space.shape가 (10, 2)라면 num_agents는 10
obs_shape = model.observation_space.shape
num_agents = obs_shape[0]  
feature_dim = obs_shape[1] # 2 (dx, dy)

print(f"모델 로드 완료! 감지된 에이전트 수: {num_agents}개, 특징 수: {feature_dim}개")


# 2. ONNX 익스포트용 래퍼 클래스 (유니티 데이터 규격 매핑)
class PolicyWrapper(torch.nn.Module):
    def __init__(self, sb3_model, agents_count):
        super().__init__()
        self.policy = sb3_model.policy
        self.num_agents = agents_count

    def forward(self, obs):
        # 1. 유니티 센티스에서 입력된 [Batch, NumAgents, 2] 구조를
        #    SB3 FlatExtractor가 처리할 수 있게 [Batch, NumAgents * 2]로 평탄화
        flat_obs = obs.view(obs.size(0), -1)
        
        # 2. 핵심 정책 신경망 통과 (결정론적 액션 평균값 추출)
        features = self.policy.extract_features(flat_obs)
        latent_pi, _ = self.policy.mlp_extractor(features)
        mean_actions = self.policy.action_net(latent_pi)
        
        # 3. 유니티 C# 단에서 NativeArray로 다시 쪼개기 편하게 
        #    최종 출력을 원래의 3차원 배치 모양 [Batch, NumAgents, 2]로 복원해서 리턴
        return mean_actions.view(-1, self.num_agents, 2)


wrapper = PolicyWrapper(model, num_agents)
wrapper.eval()

# 3. 유니티 센티스 입력 형태 정의 [Batch=1, NumAgents, Features=2]
dummy_input = torch.zeros((1, num_agents, feature_dim), dtype=torch.float32)

# 4. ONNX 변환 및 저장 (유니티 Sentis 표준 규격에 맞춤)
torch.onnx.export(
    wrapper, 
    (dummy_input,), 
    "Phase3_Policy.onnx",
    input_names=["observation"], 
    output_names=["action"],
    dynamic_axes={
        "observation": {0: "batch"}, 
        "action": {0: "batch"}
    },
    export_params=True, 
    opset_version=13, # Sentis와 호환성이 좋은 높은 opset 유지
)

print(f"Phase3_Policy.onnx 파일 생성 완료! (출력 텐서 구조: [Batch, {num_agents}, 2])")