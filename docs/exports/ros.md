# ROS 1 / ROS 2

## 会得到什么

ROS 1 和 ROS 2 分别输出独立的机器人描述功能包，包含 URDF、Visual/Collision 网格、配置和
检查报告。可以只选自己使用的 ROS 版本。

## 控制相关内容

插件可以根据已经确认的 Joint 信息生成对应描述和配置，但不会从 CAD 形状猜 PID，也不会保证
任意机器人导出后直接获得合适的控制效果。

使用控制配置前必须确认：

- Joint 名称与控制器配置一致。
- 位置、速度、力或力矩接口符合硬件或仿真插件。
- 限位和 PID 来自真实设备或经过验证的模型。
- 启动后先低风险测试运动方向和范围。

## 导出后检查

1. 打开 `config/export_report.md` 查看错误和警告。
2. 查看 `config/inertial_validation.csv`。
3. 查看 `config/mesh_manifest.csv`。
4. 在 RViz 中检查外观、坐标和 Joint。
5. 在目标 ROS/Gazebo 环境中检查控制器启动和运动。
