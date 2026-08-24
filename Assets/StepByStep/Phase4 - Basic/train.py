"""
CartPole-v1 PPO 학습 스크립트 (SB3)
- Gymnasium 표준 CartPole-v1 사용 (직접 물리 구현 X)
- 학습 후 Unity 포팅을 위해 obs 구조를 그대로 기억해둘 것:
  obs = [cart_pos, cart_vel, pole_angle, pole_angular_vel]  (4-dim)
  action = 0(왼쪽) / 1(오른쪽)  (discrete 2)
"""

import gymnasium as gym
from stable_baselines3 import PPO
from stable_baselines3.common.env_util import make_vec_env
from stable_baselines3.common.callbacks import EvalCallback
from stable_baselines3.common.monitor import Monitor

MODEL_PATH = "cartpole_ppo"
TOTAL_TIMESTEPS = 200_000
N_ENVS = 8

def main():
    # 학습용: 병렬 환경 (렌더링 없음, 속도 우선)
    train_env = make_vec_env("CartPole-v1", n_envs=N_ENVS)

    # 평가용: 별도 단일 환경 (수렴 체크용, best model 자동 저장)
    eval_env = Monitor(gym.make("CartPole-v1"))
    eval_callback = EvalCallback(
        eval_env,
        best_model_save_path="./best_model",
        log_path="./eval_logs",
        eval_freq=5000,
        n_eval_episodes=10,
        deterministic=True,
    )

    model = PPO(
        "MlpPolicy",
        train_env,
        verbose=1,
        n_steps=256,
        batch_size=64,
        gae_lambda=0.95,
        gamma=0.99,
        n_epochs=10,
        ent_coef=0.0,
        learning_rate=3e-4,
        tensorboard_log="./tb_logs",
    )

    model.learn(total_timesteps=TOTAL_TIMESTEPS, callback=eval_callback)
    model.save(MODEL_PATH)
    print(f"학습 완료. 모델 저장: {MODEL_PATH}.zip (best_model/best_model.zip 도 확인)")

if __name__ == "__main__":
    main()