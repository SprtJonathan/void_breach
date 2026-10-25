HEADER
{
	Description = "Ocean inverse FFT columns and texture assembly";
}
MODES
{
	Default();
}
CS
{
	#define FFT_COLUMNS 1
	#include "water/shaders/fft/transform.hlsl"
}
