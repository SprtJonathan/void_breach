HEADER
{
	Description = "Ocean inverse FFT rows";
}
MODES
{
	Default();
}
CS
{
	#define FFT_COLUMNS 0
	#include "water/shaders/fft/transform.hlsl"
}
