extends KineBody2D

func _physics_process(_delta: float) -> void:
	motion_vector.x = Input.get_axis("ui_left", "ui_right") * 200
	
	if Input.is_action_just_pressed("ui_accept") and is_on_floor():
		jump(600)
	
	move_kinebody()
	print(mass)
