"""
학습된 CartPole PPO 모델을 렌더링해서 눈으로 확인하는 스크립트.
reward 그래프만 믿지 말고 실제 균형 잡는 모습을 확인할 것.

사용법:
    python visualize.py                # best_model 사용
    python visualize.py cartpole_ppo   # 특정 모델 파일 사용
"""

import sys
import time
import gymnasium as gym
from stable_baselines3 import PPO

DEFAULT_MODEL = "best_model/best_model"
N_EPISODES = 5

def main():
    model_path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_MODEL
    model = PPO.load(model_path)

    env = gym.make("CartPole-v1", render_mode="human")

    for ep in range(N_EPISODES):
        obs, _ = env.reset()
        total_reward = 0
        done = False
        while not done:
            action, _ = model.predict(obs, deterministic=True)
            obs, reward, terminated, truncated, _ = env.step(int(action))
            total_reward += reward
            done = terminated or truncated
            time.sleep(0.01)  # 눈으로 보기 편하게 살짝 지연
        print(f"[Episode {ep + 1}] reward = {total_reward}")

    env.close()

if __name__ == "__main__":
    main()