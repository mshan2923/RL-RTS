"""
Phase1 정책 네트워크. obs_dim=9 (distance, dot, cross, h0~h5), action_dim=6 (6방향 로짓).

주의: LocalInferencePolicyProvider(Unity)의 OutputToAction은 argmax를 쓰므로,
학습 시에는 softmax+categorical sampling을 쓰더라도 export되는 forward()의
출력은 반드시 "6개 로짓"이어야 한다 (Unity 쪽 argmax와 형태 일치).
"""

import torch
import torch.nn as nn


class Phase1PolicyNet(nn.Module):
    def __init__(self, obs_dim=9, hidden=64, action_dim=6):
        super().__init__()
        self.body = nn.Sequential(
            nn.Linear(obs_dim, hidden),
            nn.ReLU(),
            nn.Linear(hidden, hidden),
            nn.ReLU(),
        )
        self.policy_head = nn.Linear(hidden, action_dim)  # 6방향 로짓
        self.value_head = nn.Linear(hidden, 1)            # PPO critic

    def forward(self, obs):
        features = self.body(obs)
        logits = self.policy_head(features)
        value = self.value_head(features)
        return logits, value

    def forward_policy_only(self, obs):
        """ONNX export용: 로짓만 반환 (Unity Inference Engine이 쓰는 형태)."""
        features = self.body(obs)
        return self.policy_head(features)
