# ROS 1 / ROS 2 功能包

## 会导出什么

ROS 目标生成可以作为机器人描述包继续使用的目录，包含 URDF、Visual/Collision 网格、配置文件
和校验报告。ROS 1 与 ROS 2 分开输出，用户可以只选其中一个。

## 控制相关文件

控制配置只有在已校验配置存在时才生成。插件不会根据 CAD 外形猜 PID，也不会承诺任意机器人
直接启动后就有合适的控制效果。Joint 类型、接口、限位与控制器参数必须符合真实硬件或仿真模型。

ROS 2 的固定最小测试夹具已通过手动触发的集成门禁：功能包构建、Gazebo 启动以及
`joint_state_broadcaster`、`arm_controller` 激活。该证据只覆盖测试夹具和指定环境，不代表用户
模型自动通过。

## 导出后先看什么

1. `config/export_report.md`
2. `config/inertial_validation.csv`
3. `config/mesh_manifest.csv`
4. URDF 中的 Link、Joint、transmission 或 `ros2_control` 配置
5. 目标 ROS 环境中的解析、显示、控制器启动与运动方向

详细步骤见 [Wiki Quick Start](/wiki/Quick-Start-zh-CN)。
