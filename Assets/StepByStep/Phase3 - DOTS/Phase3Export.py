import torch
import torch.nn as nn
from stable_baselines3 import PPO
import gymnasium as gym

# class ActorWrapper(nn.Module):
#     def __init__(self, policy):
#         super().__init__()
#         self.policy = policy

#     def forward(self, obs):
#         features = self.policy.features_extractor(obs)
#         latent_pi, _ = self.policy.mlp_extractor(features)
#         action_logits = self.policy.action_net(latent_pi)
#         return action_logits

# def export_ppo_to_onnx(model_path, output_path):
#     # 1. 장치 설정: GPU가 있으면 무조건 cuda, 없으면 cpu
#     device = torch.device("cpu")#! cuda 이면 동적배치 불가
#     print(f"[Info] Using device: {device}")
    
#     # 모델 로드
#     model = PPO.load(model_path)
    
#     # 2. 래퍼 적용 후 명시적으로 device 이동
#     actor_net = ActorWrapper(model.policy).to(device)
#     actor_net.eval()
    
#     # 3. dummy_input 생성 후 명시적으로 device 이동
#     obs_shape = model.observation_space.shape
#     print(f">>>>>>>>>> {obs_shape}")
#     dummy_input = torch.randn(1, *obs_shape, dtype=torch.float32).to(device)
    
#     # 엑스포트
#     batch = torch.export.Dim("batch", min=1, max=1024)
#     dynamic_shapes = {
#         "input": {0: batch}  # "input"은 input_names에 적어둔 이름과 같아야 해
#     }

#     torch.onnx.export(
#         model,
#         dummy_input,
#         "AgentActor.onnx",
#         export_params=True,
#         opset_version=18,  # 신형 익스포터는 높은 버전(18 이상)을 권장해
#         input_names=["input"],
#         output_names=["output"],
#         dynamic_shapes=dynamic_shapes,  # <--- dynamic_axes 대신 이걸 사용해!
#         dynamo=True
#     )
    
#     print(f"[Export] 변환 성공: {output_path}")
#     print(f"[Export] 입력 차원: {dummy_input.shape}")

# if __name__ == "__main__":
#     MODEL_FILE = "phase3_final_model"
#     ONNX_FILE = "AgentActor.onnx"
    
#     export_ppo_to_onnx(MODEL_FILE, ONNX_FILE)


import os
import torch as th
from stable_baselines3 import PPO

# 1. 유니티 Sentis 추론용 래퍼 클래스
class ActorWrapper(th.nn.Module):
    def __init__(self, policy):
        super().__init__()
        self.policy = policy

    def forward(self, observation: th.Tensor):
        # SB3 policy는 기본적으로 (actions, values, log_probs) 튜플을 반환해.
        # 유니티에서 캐릭터를 제어할 Action 값만 필요하니까 첫 번째 원소만 리턴할게.
        # deterministic=True를 설정해야 가장 확률이 높은 최적의 행동을 골라줘.
        actions, _, _ = self.policy(observation, deterministic=True)
        return actions

def export_ppo_to_onnx(model_path, export_path):
    if not os.path.exists(model_path):
        print(f"[Error] 학습된 모델 파일(.zip)을 찾을 수 없어: {model_path}")
        print("하단 if __name__ == '__main__': 부분의 MODEL_FILE 이름을 실제 파일명으로 수정해줘.")
        return
        
    print(f"[Info] 모델 로드 중: {model_path}")
    # 가중치를 확실하게 CPU로 올려서 안전하게 내보내기 준비
    model = PPO.load(model_path, device="cpu")
    
    # 순수 텐서 연산만 추출하기 위해 래퍼 클래스로 감싸기
    actor_wrapper = ActorWrapper(model.policy)
    actor_wrapper.eval()
    
    # 네 관측값 차원인 (8,)에 맞춰 더미 데이터 매칭 (배치 차원 포함해서 1, 8)
    dummy_input = th.zeros((1, 8), dtype=th.float32)
    
    print("[Info] ONNX 변환 시작 (동적 배치 활성화)...")
    th.onnx.export(
        actor_wrapper,
        dummy_input,
        export_path,
        export_params=True,
        opset_version=14, # 유니티 Sentis 환경과 호환성이 가장 좋은 버전
        input_names=["input"],
        output_names=["output"],
        dynamic_axes={    # 유니티에서 대량의 에이전트 배치를 유연하게 처리하기 위한 핵심 설정
            "input": {0: "batch_size"},
            "output": {0: "batch_size"}
        },
        dynamo=False      # 신형 Dynamo 대신 레거시 익스포터를 강제해서 dynamic_axes를 강제로 적용함
    )
    print(f"[Success] ONNX 변환 완료: {export_path}")

if __name__ == "__main__":
    # !!! 중요: 실제 폴더에 있는 네 학습 모델 파일명(.zip)으로 정확하게 수정해줘 !!!
    MODEL_FILE = "phase3_final_model.zip" 
    ONNX_FILE = "AgentActor.onnx"
    
    # 실제 변환 함수 호출 실행부
    export_ppo_to_onnx(MODEL_FILE, ONNX_FILE)